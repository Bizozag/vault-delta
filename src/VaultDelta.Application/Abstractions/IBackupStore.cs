using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Abstractions;

public interface IBackupStore
{
    string GetDeletedBackupRelativePath(RelativePath relativePath, SnapshotEntryKind entryKind);

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
