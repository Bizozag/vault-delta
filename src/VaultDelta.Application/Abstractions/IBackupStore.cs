using VaultDelta.Domain.Paths;

namespace VaultDelta.Application.Abstractions;

public interface IBackupStore
{
    ValueTask<string> BackupFileAsync(
        string targetRoot,
        string backupRoot,
        RelativePath relativePath,
        CancellationToken cancellationToken = default);

    string MoveDeleted(
        string targetRoot,
        string backupRoot,
        RelativePath relativePath);
}
