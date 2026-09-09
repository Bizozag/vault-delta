using VaultDelta.Application.Compare;
using VaultDelta.Desktop.Services;
using VaultDelta.Desktop.ViewModels;
using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;
using VaultDelta.Application.Apply;
using VaultDelta.Application.Patches;
using VaultDelta.Domain.Apply;

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
    public void Dropped_folders_fill_each_compare_slot_and_invalid_paths_are_ignored()
    {
        string root = Path.Combine(Path.GetTempPath(), $"vaultdelta-drop-{Guid.NewGuid():N}");
        string baseline = Directory.CreateDirectory(Path.Combine(root, "baseline")).FullName;
        string target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
        try
        {
            using CompareWorkspaceViewModel viewModel = new(new QueueFolderPicker(), new ImmediateCompareService(CreateResult()));

            Assert.True(viewModel.SetDroppedBaselinePath(baseline));
            Assert.True(viewModel.SetDroppedTargetPath(target));
            Assert.False(viewModel.SetDroppedTargetPath(Path.Combine(root, "missing")));

            Assert.Equal(baseline, viewModel.BaselinePath);
            Assert.Equal(target, viewModel.TargetPath);
            Assert.True(viewModel.CanCompare);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

    [Fact]
    public async Task Completed_comparison_can_publish_a_zip_through_the_patch_workflow()
    {
        CompareResult result = CreateResult();
        RecordingPatchWorkflow patchWorkflow = new();
        using CompareWorkspaceViewModel viewModel = new(
            new QueueFolderPicker(),
            new ImmediateCompareService(result),
            new SaveOnlyPicker("D:/out/delta.zip"),
            patchWorkflow)
        {
            BaselinePath = "C:/baseline",
            TargetPath = "C:/target",
        };
        await viewModel.CompareAsync();

        await viewModel.BuildPatchAsync();

        Assert.True(viewModel.IsPatchBuilt);
        Assert.Equal("D:/out/delta.zip", viewModel.BuiltPatchPath);
        Assert.Same(result, patchWorkflow.Comparison);
        Assert.Equal("C:/target", patchWorkflow.SourceRoot);
    }

    [Fact]
    public async Task Zip_generation_exposes_determinate_stage_percentage_and_current_file()
    {
        BlockingPatchWorkflow patchWorkflow = new();
        using CompareWorkspaceViewModel viewModel = new(
            new QueueFolderPicker(),
            new ImmediateCompareService(CreateResult()),
            new SaveOnlyPicker("D:/out/delta.zip"),
            patchWorkflow)
        {
            BaselinePath = "C:/baseline",
            TargetPath = "C:/target",
        };
        await viewModel.CompareAsync();

        Task build = viewModel.BuildPatchAsync();
        await patchWorkflow.Started.Task;
        await Task.Delay(50);

        Assert.True(viewModel.IsGeneratingPatch);
        Assert.Equal(72, viewModel.PatchBuildProgressPercentage);
        Assert.Equal("正在压缩 ZIP", viewModel.PatchBuildProgressText);
        Assert.Equal("files/Notes/Welcome.md", viewModel.PatchBuildCurrentPath);

        patchWorkflow.Release.SetResult();
        await build;
        Assert.False(viewModel.IsGeneratingPatch);
        Assert.Equal(100, viewModel.PatchBuildProgressPercentage);
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

    private sealed class SaveOnlyPicker(string outputPath) : IPatchStoragePicker
    {
        public Task<string?> SavePatchAsync(string suggestedFileName, CancellationToken cancellationToken = default) => Task.FromResult<string?>(outputPath);
        public Task<string?> OpenPatchAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> OpenJournalAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class RecordingPatchWorkflow : IPatchWorkflowService
    {
        public CompareResult? Comparison { get; private set; }
        public string? SourceRoot { get; private set; }

        public ValueTask BuildAsync(CompareResult comparison, string sourceRoot, string outputPath, IProgress<PackageBuildProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Comparison = comparison;
            SourceRoot = sourceRoot;
            progress?.Report(new PackageBuildProgress(PackageBuildStage.Completed, 100, 1, 1, "delta.zip"));
            return ValueTask.CompletedTask;
        }

        public ValueTask<PackageInspectionResult> InspectAsync(string packagePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BaselineValidationResult> ValidateAsync(PackageInspectionResult inspection, string targetRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ApplyResult> ApplyAsync(string packagePath, string targetRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RollbackResult> RollbackAsync(string journalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class BlockingPatchWorkflow : IPatchWorkflowService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask BuildAsync(CompareResult comparison, string sourceRoot, string outputPath, IProgress<PackageBuildProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            progress?.Report(new PackageBuildProgress(PackageBuildStage.Compressing, 72, 2, 5, "files/Notes/Welcome.md"));
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            progress?.Report(new PackageBuildProgress(PackageBuildStage.Completed, 100, 5, 5, "delta.zip"));
        }

        public ValueTask<PackageInspectionResult> InspectAsync(string packagePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BaselineValidationResult> ValidateAsync(PackageInspectionResult inspection, string targetRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ApplyResult> ApplyAsync(string packagePath, string targetRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RollbackResult> RollbackAsync(string journalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
