using VaultDelta.Domain.Paths;
using VaultDelta.Infrastructure.Apply;

namespace VaultDelta.Infrastructure.Tests.Apply;

public sealed class LocalApplyFileOperationsTests
{
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
