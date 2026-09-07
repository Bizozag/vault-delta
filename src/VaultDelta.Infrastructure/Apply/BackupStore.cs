using VaultDelta.Domain.Paths;
using VaultDelta.Application.Abstractions;

namespace VaultDelta.Infrastructure.Apply;

public sealed class BackupStore(IContentHasher contentHasher) : IBackupStore
{
    private readonly IContentHasher _contentHasher =
        contentHasher ?? throw new ArgumentNullException(nameof(contentHasher));

    public async ValueTask<string> BackupFileAsync(
        string targetRoot,
        string backupRoot,
        RelativePath relativePath,
        CancellationToken cancellationToken = default)
    {
        string sourcePath = ResolveWithin(targetRoot, relativePath.Value);
        string backupRelativePath = $"replaced/{relativePath.Value}";
        string destinationPath = ResolveWithin(backupRoot, backupRelativePath);
        string? parent = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrEmpty(parent))
        {
            throw new InvalidDataException($"Backup path has no parent: {backupRelativePath}.");
        }

        Directory.CreateDirectory(parent);
        await CopyFileAsync(sourcePath, destinationPath, cancellationToken).ConfigureAwait(false);
        FileInfo source = new(sourcePath);
        FileInfo destination = new(destinationPath);
        string sourceHash = await _contentHasher.ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false);
        string destinationHash = await _contentHasher.ComputeSha256Async(destinationPath, cancellationToken).ConfigureAwait(false);
        if (source.Length != destination.Length || !StringComparer.Ordinal.Equals(sourceHash, destinationHash))
        {
            File.Delete(destinationPath);
            throw new InvalidDataException($"Backup verification failed: {relativePath}.");
        }

        return backupRelativePath;
    }

    public string MoveDeleted(
        string targetRoot,
        string backupRoot,
        RelativePath relativePath)
    {
        string sourcePath = ResolveWithin(targetRoot, relativePath.Value);
        string category = Directory.Exists(sourcePath) ? "deleted-directories" : "deleted";
        string backupRelativePath = $"{category}/{relativePath.Value}";
        string destinationPath = ResolveWithin(backupRoot, backupRelativePath);
        string? parent = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrEmpty(parent))
        {
            throw new InvalidDataException($"Backup path has no parent: {backupRelativePath}.");
        }

        Directory.CreateDirectory(parent);
        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            throw new IOException($"Backup destination already exists: {backupRelativePath}");
        }

        if (Directory.Exists(sourcePath))
        {
            Directory.Move(sourcePath, destinationPath);
        }
        else
        {
            File.Move(sourcePath, destinationPath);
        }

        return backupRelativePath;
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
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await source.CopyToAsync(destination, BufferSize, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
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
}
