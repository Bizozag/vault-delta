using VaultDelta.Application.Abstractions;
using VaultDelta.Application.Snapshots;
using VaultDelta.Domain.Rules;
using VaultDelta.Infrastructure.FileSystem;
using VaultDelta.Infrastructure.Hashing;

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
            fileSystem.EnumerateEntriesAsync(_root, cancellationToken: CancellationToken.None))
        {
            entries.Add(entry);
        }

        Assert.Contains(entries, entry => entry.RelativePath == "资料" && entry.Type == FileSystemEntryType.Directory);
        Assert.Contains(entries, entry =>
            entry.RelativePath == "资料/笔记.md"
            && entry.Type == FileSystemEntryType.File
            && entry.Length == 5);
    }

    [Fact]
    public async Task Real_scanner_and_hasher_build_a_verified_inventory()
    {
        string folder = Path.Combine(_root, "Notes");
        Directory.CreateDirectory(folder);
        string notePath = Path.Combine(folder, "咖啡.md");
        await File.WriteAllTextAsync(notePath, "content", CancellationToken.None);
        SnapshotScanner scanner = new(new LocalFileSystem(), new Sha256ContentHasher());

        var inventory = await scanner.ScanAsync(
            _root,
            SnapshotRuleSet.Create("rules-v1", []),
            CancellationToken.None);

        var note = Assert.Single(inventory.Entries, entry => entry.Path.Value == "Notes/咖啡.md");
        Assert.Equal(7, note.Fingerprint!.Length);
        Assert.Equal(64, note.Fingerprint.Sha256.Length);
        Assert.StartsWith("sha256:", inventory.SnapshotId, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
