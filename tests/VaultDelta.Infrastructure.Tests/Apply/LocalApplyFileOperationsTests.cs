using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;
using VaultDelta.Infrastructure.Apply;

namespace VaultDelta.Infrastructure.Tests.Apply;

public sealed class LocalApplyFileOperationsTests
{
    [Theory]
    [InlineData(SnapshotEntryKind.File)]
    [InlineData(SnapshotEntryKind.Directory)]
    public void RemoveAdded_clears_read_only_attribute_before_deleting(SnapshotEntryKind kind)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), $"vaultdelta-readonly-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string name = kind == SnapshotEntryKind.Directory ? "added-directory" : "added-file.txt";
        string path = Path.Combine(root, name);
        try
        {
            if (kind == SnapshotEntryKind.Directory)
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                File.WriteAllText(path, "added");
            }

            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            new LocalApplyFileOperations().RemoveAdded(root, RelativePath.Parse(name), kind);

            Assert.False(File.Exists(path));
            Assert.False(Directory.Exists(path));
        }
        finally
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Move_retries_a_temporarily_locked_file_on_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), $"vaultdelta-move-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.txt");
        await File.WriteAllTextAsync(source, "content");
        try
        {
            using FileStream locked = new(source, FileMode.Open, FileAccess.Read, FileShare.None);
            Task move = Task.Run(() => new LocalApplyFileOperations().Move(
                root,
                RelativePath.Parse("source.txt"),
                RelativePath.Parse("target.txt")));
            await Task.Delay(350);
            locked.Dispose();
            await move;

            Assert.False(File.Exists(source));
            Assert.Equal("content", await File.ReadAllTextAsync(Path.Combine(root, "target.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
