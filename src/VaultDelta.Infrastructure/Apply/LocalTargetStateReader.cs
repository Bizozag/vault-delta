using VaultDelta.Application.Abstractions;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Infrastructure.Apply;

public sealed class LocalTargetStateReader(IContentHasher contentHasher) : ITargetStateReader
{
    private readonly IContentHasher _contentHasher =
        contentHasher ?? throw new ArgumentNullException(nameof(contentHasher));

    public async ValueTask<TargetEntryState> ReadAsync(
        string targetRoot,
        RelativePath relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);
        ArgumentNullException.ThrowIfNull(relativePath);

        try
        {
            string path = ResolveWithin(targetRoot, relativePath);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (FileNotFoundException)
            {
                return TargetEntryState.Missing;
            }
            catch (DirectoryNotFoundException)
            {
                return TargetEntryState.Missing;
            }

            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return TargetEntryState.Unreadable("Links and reparse points are not supported.");
            }

            if (attributes.HasFlag(FileAttributes.Directory))
            {
                return TargetEntryState.Directory;
            }

            FileInfo before = new(path);
            before.Refresh();
            string hash = await _contentHasher.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
            FileInfo after = new(path);
            after.Refresh();
            if (before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
            {
                return TargetEntryState.Unreadable("The file changed while its baseline state was inspected.");
            }

            return TargetEntryState.File(
                new FileFingerprint(after.Length, after.LastWriteTimeUtc, hash));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return TargetEntryState.Unreadable(exception.Message);
        }
    }

    private static string ResolveWithin(string rootPath, RelativePath relativePath)
    {
        string root = Path.GetFullPath(rootPath);
        string resolved = Path.GetFullPath(relativePath.Value.Replace('/', Path.DirectorySeparatorChar), root);
        string relative = Path.GetRelativePath(root, resolved);
        if (relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException($"Target path escapes the vault root: {relativePath}.");
        }

        return resolved;
    }
}
