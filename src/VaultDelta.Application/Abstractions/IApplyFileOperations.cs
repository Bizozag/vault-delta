using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Abstractions;

public interface IApplyFileOperations
{
    string GetCanonicalRoot(string path);

    ApplyTransactionPaths CreateTransactionPaths(string transactionRoot, string operationId);

    string GetTargetPath(string targetRoot, RelativePath relativePath);

    void CreateDirectory(string targetRoot, RelativePath relativePath);

    void Move(string targetRoot, RelativePath sourcePath, RelativePath targetPath);

    void RemoveAdded(string targetRoot, RelativePath relativePath, SnapshotEntryKind entryKind);

    void RestoreBackup(
        string backupRoot,
        string backupRelativePath,
        string targetRoot,
        RelativePath targetPath,
        SnapshotEntryKind entryKind);
}
