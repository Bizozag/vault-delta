using System.IO.Compression;
using VaultDelta.Application.Abstractions;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;

namespace VaultDelta.Infrastructure.Patches;

public sealed class PatchPayloadStager(IContentHasher contentHasher) : IPatchPayloadStager
{
    private readonly IContentHasher _contentHasher = contentHasher ?? throw new ArgumentNullException(nameof(contentHasher));

    public async ValueTask<IPatchPayloadSession> StageAsync(
        string packagePath,
        PatchManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(manifest);
        string root = Path.Combine(Path.GetTempPath(), $"vaultdelta-payload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            if (Directory.Exists(packagePath))
            {
                await StageDirectoryAsync(packagePath, root, manifest, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await StageZipAsync(packagePath, root, manifest, cancellationToken).ConfigureAwait(false);
            }

            return new Session(root);
        }
        catch
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    private async ValueTask StageDirectoryAsync(
        string packagePath,
        string stagingRoot,
        PatchManifest manifest,
        CancellationToken cancellationToken)
    {
        string packageRoot = Path.GetFullPath(packagePath);
        foreach (PatchOperation operation in manifest.Operations.Where(item => item.PayloadPath is not null))
        {
            string source = ResolveWithin(packageRoot, operation.PayloadPath!.Value);
            FileInfo sourceInfo = new(source);
            if (!sourceInfo.Exists || sourceInfo.LinkTarget is not null || sourceInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException($"Package payload is unavailable or unsafe: {operation.PayloadPath}.");
            }

            string destination = ResolveWithin(stagingRoot, operation.PayloadPath.Value);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
            await VerifyAsync(destination, operation, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask StageZipAsync(
        string packagePath,
        string stagingRoot,
        PatchManifest manifest,
        CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        Dictionary<string, ZipArchiveEntry> entries = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .ToDictionary(entry => entry.FullName.Replace('\\', '/'), StringComparer.Ordinal);

        foreach (PatchOperation operation in manifest.Operations.Where(item => item.PayloadPath is not null))
        {
            if (!entries.TryGetValue(operation.PayloadPath!.Value, out ZipArchiveEntry? entry))
            {
                throw new InvalidDataException($"ZIP package is missing payload {operation.PayloadPath}.");
            }

            string destination = ResolveWithin(stagingRoot, operation.PayloadPath.Value);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (Stream input = entry.Open())
            await using (FileStream output = new(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await VerifyAsync(destination, operation, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask VerifyAsync(
        string path,
        PatchOperation operation,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(path);
        string hash = await _contentHasher.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        if (file.Length != operation.NewFingerprint!.Length
            || !StringComparer.Ordinal.Equals(hash, operation.NewFingerprint.Sha256))
        {
            throw new InvalidDataException($"Staged payload verification failed: {operation.PayloadPath}.");
        }
    }

    private static string ResolveWithin(string rootPath, string relativePath)
    {
        string root = Path.GetFullPath(rootPath);
        string resolved = Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), root);
        string relative = Path.GetRelativePath(root, resolved);
        if (relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException($"Path escapes its root: {relativePath}.");
        }

        return resolved;
    }

    private sealed class Session(string root) : IPatchPayloadSession
    {
        private bool _disposed;

        public string GetPayloadPath(RelativePath payloadPath)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ResolveWithin(root, payloadPath.Value);
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                Directory.Delete(root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
