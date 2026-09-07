using VaultDelta.Application.Abstractions;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Infrastructure.Apply;

public sealed class LocalApplyFileOperations : IApplyFileOperations
{
    public string GetCanonicalRoot(string path) => Path.GetFullPath(path);

    public ApplyTransactionPaths CreateTransactionPaths(string transactionRoot, string operationId)
    {
        string operationRoot = Path.Combine(Path.GetFullPath(transactionRoot), operationId);
        Directory.CreateDirectory(operationRoot);
        return new ApplyTransactionPaths(
            operationRoot,
            Path.Combine(operationRoot, "backup"),
            Path.Combine(operationRoot, "journal.json"));
    }

    public string GetTargetPath(string targetRoot, RelativePath relativePath) =>
        ResolveWithin(targetRoot, relativePath.Value);

    public void CreateDirectory(string targetRoot, RelativePath relativePath) =>
        Directory.CreateDirectory(GetTargetPath(targetRoot, relativePath));

    public void Move(string targetRoot, RelativePath sourcePath, RelativePath targetPath)
    {
        string source = GetTargetPath(targetRoot, sourcePath);
        string target = GetTargetPath(targetRoot, targetPath);
        string? parent = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(parent))
        {
            throw new InvalidDataException($"Target path has no parent: {targetPath}.");
        }

        Directory.CreateDirectory(parent);
        if (Directory.Exists(source))
        {
            Directory.Move(source, target);
        }
        else
        {
            File.Move(source, target);
        }
    }

    public void RemoveAdded(string targetRoot, RelativePath relativePath, SnapshotEntryKind entryKind)
    {
        string path = GetTargetPath(targetRoot, relativePath);
        if (entryKind == SnapshotEntryKind.File)
        {
            File.Delete(path);
            return;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: false);
        }
    }

    public void RestoreBackup(
        string backupRoot,
        string backupRelativePath,
        string targetRoot,
        RelativePath targetPath,
        SnapshotEntryKind entryKind)
    {
        string backup = ResolveWithin(backupRoot, backupRelativePath);
        string target = GetTargetPath(targetRoot, targetPath);
        string? parent = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(parent))
        {
            throw new InvalidDataException($"Restore path has no parent: {targetPath}.");
        }

        Directory.CreateDirectory(parent);
        if (entryKind == SnapshotEntryKind.Directory)
        {
            Directory.Move(backup, target);
            return;
        }

        File.Move(backup, target, overwrite: true);
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
