using System.Runtime.CompilerServices;
using VaultDelta.Application.Abstractions;
using VaultDelta.Application.Snapshots;
using VaultDelta.Domain.Rules;

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

        var inventory = await scanner.ScanAsync("/vault", EmptyRules(), CancellationToken.None);

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

        var inventory = await scanner.ScanAsync("/vault", EmptyRules(), CancellationToken.None);

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
            await scanner.ScanAsync("/vault", EmptyRules(), CancellationToken.None));
    }

    [Fact]
    public async Task ScanAsync_rejects_links_without_hashing_them()
    {
        FakeFileSystem fileSystem = new([File("linked.md", 3) with { IsLink = true }]);
        FakeHasher hasher = new();
        SnapshotScanner scanner = new(fileSystem, hasher);

        await Assert.ThrowsAsync<SnapshotScanException>(async () =>
            await scanner.ScanAsync("/vault", EmptyRules(), CancellationToken.None));

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
            await scanner.ScanAsync("/vault", EmptyRules(), cancellation.Token));
    }

    [Fact]
    public async Task ScanAsync_skips_excluded_files_before_hashing()
    {
        FakeFileSystem fileSystem = new([File(".trash/deleted.md", 3), File("Notes/keep.md", 4)]);
        FakeHasher hasher = new();
        SnapshotScanner scanner = new(fileSystem, hasher);

        var inventory = await scanner.ScanAsync("/vault", ObsidianDefaultRules.Create(), CancellationToken.None);

        Assert.Equal("Notes/keep.md", Assert.Single(inventory.Entries).Path.Value);
        Assert.Equal(1, hasher.CallCount);
        Assert.False(fileSystem.ShouldDescend!(".trash"));
        Assert.True(fileSystem.ShouldDescend("Notes"));
    }

    [Fact]
    public async Task ScanAsync_reports_each_included_entry_after_it_is_complete()
    {
        FakeFileSystem fileSystem = new([Directory("Notes"), File("Notes/keep.md", 4)]);
        SnapshotScanner scanner = new(fileSystem, new FakeHasher());
        List<SnapshotScanProgress> reports = [];

        await scanner.ScanAsync(
            "/vault",
            EmptyRules(),
            new InlineProgress<SnapshotScanProgress>(reports.Add),
            CancellationToken.None);

        Assert.Equal([1, 2], reports.Select(report => report.ProcessedEntries));
        Assert.Equal(["Notes", "Notes/keep.md"], reports.Select(report => report.CurrentPath));
    }

    private static SnapshotRuleSet EmptyRules() => SnapshotRuleSet.Create("rules-v1", []);

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

        public Func<string, bool>? ShouldDescend { get; private set; }

        public bool DirectoryExists(string path) => true;

        public async IAsyncEnumerable<FileSystemEntryMetadata> EnumerateEntriesAsync(
            string rootPath,
            Func<string, bool>? shouldDescend = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ShouldDescend = shouldDescend;
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

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
