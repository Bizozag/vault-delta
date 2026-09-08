using VaultDelta.Application.Compare;
using VaultDelta.Desktop.Services;
using VaultDelta.Desktop.ViewModels;
using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.EndToEnd.Tests.Desktop;

public sealed class CompareWorkspaceViewModelTests
{
    [Fact]
    public async Task Folder_picker_cancellation_preserves_existing_path()
    {
        QueueFolderPicker picker = new([null]);
        using CompareWorkspaceViewModel viewModel = new(picker, new ImmediateCompareService(CreateResult()));
        viewModel.BaselinePath = "C:/existing";

        await viewModel.SelectBaselineAsync();

        Assert.Equal("C:/existing", viewModel.BaselinePath);
    }

    [Fact]
    public void Two_paths_enable_compare()
    {
        using CompareWorkspaceViewModel viewModel = new(new QueueFolderPicker(), new ImmediateCompareService(CreateResult()));

        viewModel.BaselinePath = "C:/baseline";
        Assert.False(viewModel.CanCompare);
        viewModel.TargetPath = "C:/target";

        Assert.True(viewModel.CanCompare);
        Assert.Equal(CompareSessionState.Ready, viewModel.State);
    }

    [Fact]
    public async Task Running_comparison_can_be_cancelled_without_partial_result()
    {
        BlockingCompareService service = new();
        using CompareWorkspaceViewModel viewModel = ReadyViewModel(service);

        Task comparison = viewModel.CompareAsync();
        await service.Started.Task;
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.CanCompare);

        viewModel.Cancel();
        await comparison;

        Assert.Equal(CompareSessionState.Cancelled, viewModel.State);
        Assert.False(viewModel.HasResult);
        Assert.Equal("C:/baseline", viewModel.BaselinePath);
        Assert.Equal("C:/target", viewModel.TargetPath);
    }

    [Fact]
    public async Task Completed_comparison_exposes_summary_risks_transfer_size_and_filters()
    {
        CompareResult result = CreateResult();
        using CompareWorkspaceViewModel viewModel = ReadyViewModel(new ImmediateCompareService(result));

        await viewModel.CompareAsync();

        Assert.Equal(CompareSessionState.Completed, viewModel.State);
        Assert.Equal(1, viewModel.AddedCount);
        Assert.Equal(1, viewModel.ModifiedCount);
        Assert.Equal(1, viewModel.DeletedCount);
        Assert.Equal(1, viewModel.RenamedCount);
        Assert.Equal(3, viewModel.RiskCount);
        Assert.Equal("2 个文件 · 1.5 KB", viewModel.TransferSizeText);
        Assert.Equal(4, viewModel.FilteredEntries.Count);

        viewModel.SetFilterCommand.Execute("Risks");
        Assert.Equal(3, viewModel.FilteredEntries.Count);
        Assert.All(viewModel.FilteredEntries, item => Assert.True(item.IsRisk));

        viewModel.SetFilterCommand.Execute("Added");
        Assert.Single(viewModel.FilteredEntries);
        Assert.Equal(DiffEntryType.Added, viewModel.FilteredEntries[0].Type);
    }

    [Fact]
    public async Task Failed_comparison_keeps_paths_and_shows_readable_error()
    {
        using CompareWorkspaceViewModel viewModel = ReadyViewModel(
            new ThrowingCompareService(new DirectoryNotFoundException()));

        await viewModel.CompareAsync();

        Assert.Equal(CompareSessionState.Error, viewModel.State);
        Assert.Contains("不存在", viewModel.ErrorMessage);
        Assert.Equal("C:/baseline", viewModel.BaselinePath);
        Assert.Equal("C:/target", viewModel.TargetPath);
        Assert.False(viewModel.HasResult);
    }

    private static CompareWorkspaceViewModel ReadyViewModel(ICompareService service) =>
        new(new QueueFolderPicker(), service)
        {
            BaselinePath = "C:/baseline",
            TargetPath = "C:/target",
        };

    internal static CompareResult CreateResult()
    {
        SnapshotInventory baseline = SnapshotInventory.Create(
            "rules",
            [
                File("modified.md", 100, 'a'),
                File("deleted.md", 40, 'b'),
                File("old.md", 30, 'c'),
                File("same.md", 10, 'd'),
            ]);
        SnapshotInventory target = SnapshotInventory.Create(
            "rules",
            [
                File("modified.md", 512, 'e'),
                File(".obsidian/plugins/example/main.js", 1024, 'f'),
                File("new.md", 30, 'c'),
                File("same.md", 10, 'd'),
            ]);
        DiffSet differences = DiffEngine.Compare(baseline, target);
        CompareSummary summary = new(1, 1, 1, 1, 1, 2, 1536, 3);
        return new CompareResult(baseline, target, differences, summary);
    }

    private static SnapshotEntry File(string path, long length, char hash) =>
        new(RelativePath.Parse(path), SnapshotEntryKind.File, new FileFingerprint(length, DateTimeOffset.UnixEpoch, new string(hash, 64)));

    private sealed class QueueFolderPicker(params string?[] selections) : IFolderPicker
    {
        private readonly Queue<string?> _selections = new(selections);
        public Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default) =>
            Task.FromResult(_selections.Count == 0 ? null : _selections.Dequeue());
    }

    internal sealed class ImmediateCompareService(CompareResult result) : ICompareService
    {
        public ValueTask<CompareResult> CompareAsync(string baselinePath, string targetPath, IProgress<CompareProgress>? progress = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(result);
    }

    private sealed class BlockingCompareService : ICompareService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<CompareResult> CompareAsync(string baselinePath, string targetPath, IProgress<CompareProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }

    private sealed class ThrowingCompareService(Exception exception) : ICompareService
    {
        public ValueTask<CompareResult> CompareAsync(string baselinePath, string targetPath, IProgress<CompareProgress>? progress = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<CompareResult>(exception);
    }
}
