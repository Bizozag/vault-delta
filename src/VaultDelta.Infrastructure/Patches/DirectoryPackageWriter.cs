using System.Text;
using VaultDelta.Application.Abstractions;
using VaultDelta.Domain.Patches;

namespace VaultDelta.Infrastructure.Patches;

public sealed class DirectoryPackageWriter(IContentHasher contentHasher) : IPatchPackageWriter
{
    private readonly IContentHasher _contentHasher =
        contentHasher ?? throw new ArgumentNullException(nameof(contentHasher));

    public async ValueTask WriteAsync(
        PatchManifest manifest,
        string sourceRoot,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();

        string canonicalSource = Path.GetFullPath(sourceRoot);
        string canonicalOutput = Path.GetFullPath(outputPath);
        ValidateLocations(canonicalSource, canonicalOutput);

        if (!Directory.Exists(canonicalSource))
        {
            throw new DirectoryNotFoundException($"Source snapshot does not exist: {canonicalSource}");
        }

        if (Directory.Exists(canonicalOutput) || File.Exists(canonicalOutput))
        {
            throw new IOException($"Patch output already exists: {canonicalOutput}");
        }

        string? outputParent = Path.GetDirectoryName(canonicalOutput);
        if (string.IsNullOrEmpty(outputParent))
        {
            throw new ArgumentException("Patch output must have a parent directory.", nameof(outputPath));
        }

        Directory.CreateDirectory(outputParent);
        string stagingPath = Path.Combine(
            outputParent,
            $".{Path.GetFileName(canonicalOutput)}.incomplete-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagingPath);
            await CopyPayloadsAsync(manifest, canonicalSource, stagingPath, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(
                Path.Combine(stagingPath, "manifest.json"),
                PatchManifestJson.Serialize(manifest),
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(stagingPath, "README.txt"),
                CreateReadme(manifest),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);

            Directory.Move(stagingPath, canonicalOutput);
        }
        catch
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }

            throw;
        }
    }

    private async ValueTask CopyPayloadsAsync(
        PatchManifest manifest,
        string sourceRoot,
        string stagingPath,
        CancellationToken cancellationToken)
    {
        foreach (PatchOperation operation in manifest.Operations.Where(item => item.PayloadPath is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sourcePath = ResolveWithin(sourceRoot, operation.TargetPath!.Value);
            string payloadPath = ResolveWithin(stagingPath, operation.PayloadPath!.Value);
            string? payloadParent = Path.GetDirectoryName(payloadPath);
            if (string.IsNullOrEmpty(payloadParent))
            {
                throw new InvalidDataException($"Payload has no parent directory: {operation.PayloadPath}.");
            }

            Directory.CreateDirectory(payloadParent);
            await CopyFileAsync(sourcePath, payloadPath, cancellationToken).ConfigureAwait(false);

            FileInfo copied = new(payloadPath);
            string copiedHash = await _contentHasher
                .ComputeSha256Async(payloadPath, cancellationToken)
                .ConfigureAwait(false);
            if (copied.Length != operation.NewFingerprint!.Length
                || !StringComparer.Ordinal.Equals(copiedHash, operation.NewFingerprint.Sha256))
            {
                throw new InvalidDataException($"Payload verification failed: {operation.TargetPath}.");
            }
        }
    }

    private static async ValueTask CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        const int BufferSize = 1024 * 1024;
        await using FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await source.CopyToAsync(destination, BufferSize, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveWithin(string root, string relativePath)
    {
        string fullPath = Path.GetFullPath(
            relativePath.Replace('/', Path.DirectorySeparatorChar),
            root);
        string relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException($"Path escapes the package boundary: {relativePath}.");
        }

        return fullPath;
    }

    private static void ValidateLocations(string sourceRoot, string outputPath)
    {
        string relative = Path.GetRelativePath(sourceRoot, outputPath);
        bool outputInsideSource = relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
        if (outputInsideSource)
        {
            throw new ArgumentException("Patch output cannot be located inside the source snapshot.", nameof(outputPath));
        }
    }

    private static string CreateReadme(PatchManifest manifest) =>
        $"Vault Delta patch {manifest.PatchId}{Environment.NewLine}"
        + $"Schema: {manifest.SchemaVersion}{Environment.NewLine}"
        + $"Operations: {manifest.Operations.Count}{Environment.NewLine}"
        + "Inspect this package with Vault Delta before applying it."
        + Environment.NewLine;
}
