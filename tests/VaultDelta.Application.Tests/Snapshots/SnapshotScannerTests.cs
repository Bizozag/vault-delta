using System.Runtime.CompilerServices;
using VaultDelta.Application.Abstractions;
using VaultDelta.Application.Snapshots;

namespace VaultDelta.Application.Tests.Snapshots;

public sealed class SnapshotScannerTests
{
    [Fact]
    public async Task ScanAsync_builds_a_sorted_inventory()
    {
        FakeFileSystem fileSystem = new(
            [
                File("Z.md", 3),
                Directory("Folder"),
                File("Folder/A.md", 5),
            ]);
        SnapshotScanner scanner = new(fileSystem, new FakeHasher());

        var inventory = await scanner.ScanAsync("/vault", "rules-v1", CancellationToken.None);

        Assert.Equal(["Folder", "Folder/A.md", "Z.md"], inventory.Entries.Select(entry => entry.Path.Value));
        Assert.Equal(2, fileSystem.MetadataReadCount);
    }

    [Fact]
    public async Task ScanAsync_retries_once_when_a_file_changes_during_hashing()
    {
        FakeFileSystem fileSystem = new([File("note.md", 3)]);
        fileSystem.MetadataResponses.Enqueue(File("note.md", 4));
        fileSystem.MetadataResponses.Enqueue(File("note.md", 4));
        FakeHasher hasher = new();
        SnapshotScanner scanner = new(fileSystem, hasher);

        var inventory = await scanner.ScanAsync("/vault", "rules-v1", CancellationToken.None);

        Assert.Equal(2, hasher.CallCount);
        Assert.Equal(4, inventory.Entries.Single().Fingerprint!.Length);
    }

    [Fact]
    public async Task ScanAsync_rejects_a_file_that_keeps_changing()
    {
        FakeFileSystem fileSystem = new([File("note.md", 3)]);
        fileSystem.MetadataResponses.Enqueue(File("note.md", 4));
        fileSystem.MetadataResponses.Enqueue(File("note.md", 5));
        SnapshotScanner scanner = new(fileSystem, new FakeHasher());

        await Assert.ThrowsAsync<SnapshotScanException>(async () =>
            await scanner.ScanAsync("/vault", "rules-v1", CancellationToken.None));
    }

    [Fact]
    public async Task ScanAsync_rejects_links_without_hashing_them()
    {
        FakeFileSystem fileSystem = new([File("linked.md", 3) with { IsLink = true }]);
        FakeHasher hasher = new();
        SnapshotScanner scanner = new(fileSystem, hasher);

        await Assert.ThrowsAsync<SnapshotScanException>(async () =>
            await scanner.ScanAsync("/vault", "rules-v1", CancellationToken.None));

        Assert.Equal(0, hasher.CallCount);
    }

    [Fact]
    public async Task ScanAsync_honors_cancellation()
    {
        FakeFileSystem fileSystem = new([File("note.md", 3)]);
        SnapshotScanner scanner = new(fileSystem, new FakeHasher());
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await scanner.ScanAsync("/vault", "rules-v1", cancellation.Token));
    }

    private static FileSystemEntryMetadata File(string path, long length) =>
        new($"/vault/{path}", path, FileSystemEntryType.File, length, DateTimeOffset.UnixEpoch, false);

    private static FileSystemEntryMetadata Directory(string path) =>
        new($"/vault/{path}", path, FileSystemEntryType.Directory, 0, DateTimeOffset.UnixEpoch, false);

    private sealed class FakeHasher : IContentHasher
    {
        public int CallCount { get; private set; }

        public ValueTask<string> ComputeSha256Async(string fullPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult(new string('a', 64));
        }
    }

    private sealed class FakeFileSystem(IReadOnlyList<FileSystemEntryMetadata> entries) : IFileSystem
    {
        public Queue<FileSystemEntryMetadata> MetadataResponses { get; } = new();

        public int MetadataReadCount { get; private set; }

        public bool DirectoryExists(string path) => true;

        public async IAsyncEnumerable<FileSystemEntryMetadata> EnumerateEntriesAsync(
            string rootPath,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (FileSystemEntryMetadata entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return entry;
                await Task.Yield();
            }
        }

        public ValueTask<FileSystemEntryMetadata> GetEntryMetadataAsync(
            string fullPath,
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetadataReadCount++;
            FileSystemEntryMetadata metadata = MetadataResponses.Count > 0
                ? MetadataResponses.Dequeue()
                : entries.Single(entry => entry.FullPath == fullPath);
            return ValueTask.FromResult(metadata);
        }
    }
}
