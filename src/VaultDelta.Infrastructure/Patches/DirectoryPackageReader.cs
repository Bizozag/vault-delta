using VaultDelta.Application.Abstractions;

namespace VaultDelta.Infrastructure.Patches;

public sealed class DirectoryPackageReader(IContentHasher contentHasher) : IPatchPackageReader
{
    private readonly IContentHasher _contentHasher =
        contentHasher ?? throw new ArgumentNullException(nameof(contentHasher));

    public async ValueTask<PatchPackageContent> ReadAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        string root = Path.GetFullPath(packagePath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Package directory does not exist: {root}");
        }

        string manifestPath = Path.Combine(root, "manifest.json");
        byte[] manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        List<PatchPackageEntry> entries = [];

        foreach (string filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo file = new(filePath);
            if (file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException($"Package contains a link: {filePath}.");
            }

            string relativePath = Path.GetRelativePath(root, filePath).Replace('\\', '/');
            string sha256 = await _contentHasher
                .ComputeSha256Async(filePath, cancellationToken)
                .ConfigureAwait(false);
            entries.Add(new PatchPackageEntry(relativePath, file.Length, sha256));
        }

        return new PatchPackageContent(PatchManifestJson.Deserialize(manifestBytes), entries);
    }
}
