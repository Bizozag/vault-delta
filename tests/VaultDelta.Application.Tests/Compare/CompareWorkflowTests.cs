using System.Runtime.CompilerServices;
using VaultDelta.Application.Abstractions;
using VaultDelta.Application.Compare;
using VaultDelta.Application.Snapshots;
using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Rules;

namespace VaultDelta.Application.Tests.Compare;

public sealed class CompareWorkflowTests
{
    [Fact]
    public async Task RunAsync_scans_both_roots_reports_stages_and_summarizes_changes()
    {
        RootedFileSystem fileSystem = new(
            new Dictionary<string, IReadOnlyList<FileSystemEntryMetadata>>
            {
                [Root("baseline")] =
                [
                    File(Root("baseline"), "same.md", 2, "same"),
                    File(Root("baseline"), "modified.md", 2, "old"),
                    File(Root("baseline"), "deleted.md", 4, "gone"),
                    File(Root("baseline"), "old-name.md", 3, "move"),
                ],
                [Root("target")] =
                [
                    File(Root("target"), "same.md", 2, "same"),
                    File(Root("target"), "modified.md", 5, "new"),
                    File(Root("target"), "added.md", 7, "add"),
                    File(Root("target"), "new-name.md", 3, "move"),
                    File(Root("target"), ".obsidian/plugins/x/main.js", 9, "plugin"),
                ],
            });
        CompareWorkflow workflow = new(new SnapshotScanner(fileSystem, new PathHasher(fileSystem)));
        List<CompareProgress> reports = [];

        CompareResult result = await workflow.RunAsync(
            new CompareRequest(Root("baseline"), Root("target"), EmptyRules()),
            new InlineProgress<CompareProgress>(reports.Add));

        Assert.Equal(2, result.Summary.AddedCount);
        Assert.Equal(1, result.Summary.ModifiedCount);
        Assert.Equal(1, result.Summary.DeletedCount);
        Assert.Equal(1, result.Summary.RenamedCount);
        Assert.Equal(1, result.Summary.UnchangedCount);
        Assert.Equal(3, result.Summary.TransferFileCount);
        Assert.Equal(21, result.Summary.TransferBytes);
        Assert.Equal(3, result.Summary.RiskCount);
        Assert.Equal(
            [CompareStage.ScanningBaseline, CompareStage.ScanningTarget, CompareStage.Comparing, CompareStage.Completed],
            reports.Select(report => report.Stage).Distinct());
    }

    [Fact]
    public async Task RunAsync_rejects_equal_or_nested_roots_before_scanning()
    {
        RootedFileSystem fileSystem = new(
            new Dictionary<string, IReadOnlyList<FileSystemEntryMetadata>>());
        CompareWorkflow workflow = new(new SnapshotScanner(fileSystem, new PathHasher(fileSystem)));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await workflow.RunAsync(new CompareRequest(Root("same"), Root("same"), EmptyRules())));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await workflow.RunAsync(new CompareRequest(Root("parent"), Path.Combine(Root("parent"), "child"), EmptyRules())));

        Assert.Equal(0, fileSystem.EnumerationCount);
    }

    [Fact]
    public async Task RunAsync_cancellation_during_baseline_never_starts_target_or_returns_a_partial_result()
    {
        using CancellationTokenSource cancellation = new();
        RootedFileSystem fileSystem = new(
            new Dictionary<string, IReadOnlyList<FileSystemEntryMetadata>>
            {
                [Root("baseline")] = [File(Root("baseline"), "one.md", 1, "one")],
                [Root("target")] = [File(Root("target"), "two.md", 1, "two")],
            },
            onYield: () => cancellation.Cancel());
        CompareWorkflow workflow = new(new SnapshotScanner(fileSystem, new PathHasher(fileSystem)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await workflow.RunAsync(
                new CompareRequest(Root("baseline"), Root("target"), EmptyRules()),
                cancellationToken: cancellation.Token));

        Assert.Equal([Root("baseline")], fileSystem.EnumeratedRoots);
    }

    private static string Root(string name) => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "vault-delta-tests", name));

    private static SnapshotRuleSet EmptyRules() => SnapshotRuleSet.Create("rules-v1", []);

    private static FileSystemEntryMetadata File(string root, string path, long length, string content)
    {
        string fullPath = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        ContentTags[fullPath] = content;
        return new FileSystemEntryMetadata(
            fullPath,
            path,
            FileSystemEntryType.File,
            length,
            DateTimeOffset.UnixEpoch,
            false);
    }

    private static Dictionary<string, string> ContentTags { get; } = new(StringComparer.Ordinal);

    private sealed class PathHasher(RootedFileSystem fileSystem) : IContentHasher
    {
        public ValueTask<string> ComputeSha256Async(string fullPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = fileSystem.AllEntries.Single(entry => entry.FullPath == fullPath);
            string tag = ContentTags[fullPath];
            return ValueTask.FromResult(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(tag))));
        }
    }

    private sealed class RootedFileSystem(
        IReadOnlyDictionary<string, IReadOnlyList<FileSystemEntryMetadata>> entriesByRoot,
        Action? onYield = null) : IFileSystem
    {
        public int EnumerationCount { get; private set; }
        public List<string> EnumeratedRoots { get; } = [];
        public IEnumerable<FileSystemEntryMetadata> AllEntries => entriesByRoot.Values.SelectMany(entries => entries);
        public bool DirectoryExists(string path) => entriesByRoot.ContainsKey(path);

        public async IAsyncEnumerable<FileSystemEntryMetadata> EnumerateEntriesAsync(
            string rootPath,
            Func<string, bool>? shouldDescend = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            EnumerationCount++;
            EnumeratedRoots.Add(rootPath);
            foreach (FileSystemEntryMetadata entry in entriesByRoot[rootPath])
            {
                cancellationToken.ThrowIfCancellationRequested();
                onYield?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                yield return entry;
                await Task.Yield();
            }
        }

        public ValueTask<FileSystemEntryMetadata> GetEntryMetadataAsync(
            string fullPath,
            string relativePath,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AllEntries.Single(entry => entry.FullPath == fullPath));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
