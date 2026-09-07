using VaultDelta.Domain.Paths;
using VaultDelta.Infrastructure.Apply;
using VaultDelta.Infrastructure.Hashing;

namespace VaultDelta.Infrastructure.Tests.Apply;

public sealed class BackupStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vaultdelta-backup-{Guid.NewGuid():N}");

    public BackupStoreTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task BackupFileAsync_preserves_relative_structure_and_content()
    {
        string targetRoot = Path.Combine(_root, "vault");
        string backupRoot = Path.Combine(_root, "backup");
        string targetFile = Path.Combine(targetRoot, "Notes", "note.md");
        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        await File.WriteAllTextAsync(targetFile, "before", CancellationToken.None);
        BackupStore store = new(new Sha256ContentHasher());

        string backupRelativePath = await store.BackupFileAsync(
            targetRoot,
            backupRoot,
            RelativePath.Parse("Notes/note.md"),
            CancellationToken.None);

        Assert.Equal("replaced/Notes/note.md", backupRelativePath);
        Assert.Equal(
            "before",
            await File.ReadAllTextAsync(Path.Combine(backupRoot, "replaced", "Notes", "note.md"), CancellationToken.None));
        Assert.Equal("before", await File.ReadAllTextAsync(targetFile, CancellationToken.None));
    }

    [Fact]
    public async Task BackupFileAsync_rejects_duplicate_backup_paths()
    {
        string targetRoot = Path.Combine(_root, "vault-duplicate");
        string backupRoot = Path.Combine(_root, "backup-duplicate");
        Directory.CreateDirectory(targetRoot);
        await File.WriteAllTextAsync(Path.Combine(targetRoot, "note.md"), "before", CancellationToken.None);
        BackupStore store = new(new Sha256ContentHasher());
        RelativePath path = RelativePath.Parse("note.md");

        await store.BackupFileAsync(targetRoot, backupRoot, path, CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(async () =>
            await store.BackupFileAsync(targetRoot, backupRoot, path, CancellationToken.None));
    }

    [Fact]
    public async Task MoveDeletedAsync_moves_content_into_deleted_area()
    {
        string targetRoot = Path.Combine(_root, "vault-delete");
        string backupRoot = Path.Combine(_root, "backup-delete");
        Directory.CreateDirectory(targetRoot);
        string target = Path.Combine(targetRoot, "old.md");
        await File.WriteAllTextAsync(target, "old", CancellationToken.None);
        BackupStore store = new(new Sha256ContentHasher());

        string backupRelativePath = store.MoveDeleted(
            targetRoot,
            backupRoot,
            RelativePath.Parse("old.md"));

        Assert.False(File.Exists(target));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(backupRoot, backupRelativePath), CancellationToken.None));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
