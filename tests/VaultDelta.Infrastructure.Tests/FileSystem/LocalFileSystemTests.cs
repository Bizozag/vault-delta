using VaultDelta.Application.Abstractions;
using VaultDelta.Infrastructure.FileSystem;

namespace VaultDelta.Infrastructure.Tests.FileSystem;

public sealed class LocalFileSystemTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vaultdelta-{Guid.NewGuid():N}");

    public LocalFileSystemTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task EnumerateEntriesAsync_returns_relative_files_and_directories()
    {
        string folder = Path.Combine(_root, "资料");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(
            Path.Combine(folder, "笔记.md"),
            "hello",
            CancellationToken.None);
        LocalFileSystem fileSystem = new();

        List<FileSystemEntryMetadata> entries = [];
        await foreach (FileSystemEntryMetadata entry in
            fileSystem.EnumerateEntriesAsync(_root, CancellationToken.None))
        {
            entries.Add(entry);
        }

        Assert.Contains(entries, entry => entry.RelativePath == "资料" && entry.Type == FileSystemEntryType.Directory);
        Assert.Contains(entries, entry =>
            entry.RelativePath == "资料/笔记.md"
            && entry.Type == FileSystemEntryType.File
            && entry.Length == 5);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
