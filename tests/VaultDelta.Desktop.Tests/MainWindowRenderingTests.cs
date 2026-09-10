using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VaultDelta.Application.Compare;
using VaultDelta.Desktop.Services;
using VaultDelta.Desktop.ViewModels;
using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;
using VaultDelta.Application.Apply;
using VaultDelta.Application.Patches;
using VaultDelta.Domain.Apply;
using VaultDelta.Domain.Patches;

namespace VaultDelta.Desktop.Tests;

public sealed class MainWindowRenderingTests
{
    [AvaloniaFact]
    public void Default_shell_renders_create_package_page_at_minimum_supported_size()
    {
        MainWindow window = new()
        {
            Width = 1024,
            Height = 680,
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.FindControl<ScrollViewer>("ComparePage")!.IsVisible);
        Assert.False(window.FindControl<ScrollViewer>("PatchesPage")!.IsVisible);
        Assert.Null(window.FindControl<ScrollViewer>("ProfilesPage"));
        Assert.Null(window.FindControl<ScrollViewer>("SettingsPage"));
        Assert.True(window.Bounds.Width >= 1024);
        Assert.True(window.Bounds.Height >= 680);
        Assert.Null(window.FindControl<Button>("ProfilesNavigation"));
        Assert.NotNull(window.FindControl<Button>("CompareNavigation"));
        Assert.NotNull(window.FindControl<Button>("PatchesNavigation"));
        Assert.NotNull(window.FindControl<Border>("AdvancedScopePanel"));
        Assert.False(window.FindControl<Border>("AdvancedScopeContent")!.IsVisible);
        Assert.True(DragDrop.GetAllowDrop(window.FindControl<Border>("BaselineDropZone")!));
        Assert.True(DragDrop.GetAllowDrop(window.FindControl<Border>("TargetDropZone")!));
        Assert.True(DragDrop.GetAllowDrop(window.FindControl<Border>("PatchPackageDropZone")!));
        Assert.True(DragDrop.GetAllowDrop(window.FindControl<Border>("PatchTargetDropZone")!));
        Assert.Equal("v0.1.3", ((MainWindowViewModel)window.DataContext!).VersionText);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "准备就绪");
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "未开始事务");
        window.Close();
    }

    [AvaloniaFact]
    public void Navigation_buttons_switch_the_visible_page()
    {
        MainWindow window = new();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.FindControl<ScrollViewer>("ComparePage")!.IsVisible);
        Assert.Equal(0, ((MainWindowViewModel)window.DataContext!).SwitchIndicatorOffset);

        window.FindControl<Button>("PatchesNavigation")!.Command!.Execute("Patches");
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.FindControl<ScrollViewer>("PatchesPage")!.IsVisible);
        Assert.False(window.FindControl<ScrollViewer>("ComparePage")!.IsVisible);
        Assert.Equal(176, ((MainWindowViewModel)window.DataContext!).SwitchIndicatorOffset);

        window.Close();
    }

    [AvaloniaFact]
    public void All_shell_pages_render_non_empty_preview_images()
    {
        string outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "vaultdelta-ui-preview");
        Directory.CreateDirectory(outputDirectory);
        MainWindow window = new()
        {
            Width = 1280,
            Height = 780,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        SaveFrame(window, Path.Combine(outputDirectory, "create-package.png"));
        window.FindControl<Button>("ToggleScopePanelButton")!.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.FindControl<Border>("AdvancedScopeContent")!.IsVisible);
        SaveFrame(window, Path.Combine(outputDirectory, "create-package-advanced-scope.png"));
        window.FindControl<Button>("PatchesNavigation")!.Command!.Execute("Patches");
        Dispatcher.UIThread.RunJobs();
        SaveFrame(window, Path.Combine(outputDirectory, "patches.png"));
        Assert.All(
            Directory.GetFiles(outputDirectory, "*.png"),
            path => Assert.True(new FileInfo(path).Length > 10_000, $"Rendered preview is unexpectedly small: {path}"));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Compare_completed_state_renders_a_non_empty_review_preview()
    {
        CompareResult result = CreateResult();
        MainWindowViewModel shell = new(new NullFolderPicker(), new ImmediateCompareService(result));
        shell.NavigateCommand.Execute("Compare");
        shell.Compare.BaselinePath = "D:/Vault/baseline";
        shell.Compare.TargetPath = "D:/Vault/target";
        await shell.Compare.CompareAsync();
        MainWindow window = new(shell)
        {
            Width = 1280,
            Height = 780,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.FindControl<StackPanel>("CompareResultPanel")!.IsVisible);
        Assert.False(window.FindControl<Border>("CompareEmptyPanel")!.IsVisible);
        SaveFrame(window, Path.Combine(Path.GetTempPath(), "vaultdelta-ui-preview", "compare-completed.png"));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Compare_scanning_and_error_states_render_their_boundaries()
    {
        BlockingCompareService blocking = new();
        MainWindowViewModel scanningShell = new(new NullFolderPicker(), blocking);
        scanningShell.NavigateCommand.Execute("Compare");
        scanningShell.Compare.BaselinePath = "D:/Vault/baseline";
        scanningShell.Compare.TargetPath = "D:/Vault/target";
        Task comparison = scanningShell.Compare.CompareAsync();
        await blocking.Started.Task;
        MainWindow scanningWindow = new(scanningShell);
        scanningWindow.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(scanningWindow.FindControl<Border>("ScanningPanel")!.IsVisible);
        Assert.True(scanningWindow.FindControl<Button>("CancelCompareButton")!.IsVisible);
        scanningShell.Compare.Cancel();
        await comparison;
        scanningWindow.Close();

        MainWindowViewModel errorShell = new(new NullFolderPicker(), new ThrowingCompareService());
        errorShell.NavigateCommand.Execute("Compare");
        errorShell.Compare.BaselinePath = "D:/Vault/missing";
        errorShell.Compare.TargetPath = "D:/Vault/target";
        await errorShell.Compare.CompareAsync();
        MainWindow errorWindow = new(errorShell);
        errorWindow.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(errorWindow.FindControl<Border>("CompareErrorPanel")!.IsVisible);
        Assert.False(errorWindow.FindControl<StackPanel>("CompareResultPanel")!.IsVisible);
        errorWindow.Close();
    }

    [AvaloniaFact]
    public async Task Zip_generation_renders_determinate_progress_panel()
    {
        ProgressPatchWorkflow workflow = new();
        MainWindowViewModel shell = new(
            new NullFolderPicker(),
            new ImmediateCompareService(CreateResult()),
            new BuildStoragePicker("D:/Transfer/delta.zip"),
            workflow);
        shell.NavigateCommand.Execute("Compare");
        shell.Compare.BaselinePath = "D:/Vault/baseline";
        shell.Compare.TargetPath = "D:/Vault/target";
        await shell.Compare.CompareAsync();

        Task build = shell.Compare.BuildPatchAsync();
        await workflow.Started.Task;
        await Task.Delay(50);
        MainWindow window = new(shell)
        {
            Width = 1280,
            Height = 780,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Border progressPanel = window.FindControl<Border>("PatchBuildProgressPanel")!;
        Assert.True(progressPanel.IsVisible);
        Assert.DoesNotContain(progressPanel.GetVisualAncestors(), ancestor => ancestor is ScrollViewer);
        Assert.False(window.FindControl<Border>("ScanningPanel")!.IsVisible);
        Assert.Equal(72, shell.Compare.PatchBuildProgressPercentage);
        SaveFrame(window, Path.Combine(Path.GetTempPath(), "vaultdelta-ui-preview", "zip-progress.png"));

        workflow.Release.SetResult();
        await build;
        window.Close();
    }

    [AvaloniaFact]
    public async Task Patch_gate_conflict_and_recovery_states_render()
    {
        PackageInspectionResult inspection = CreateInspection();
        PatchUiWorkflow workflow = new(
            inspection,
            new BaselineValidationResult(
                [new BaselineConflict(RelativePath.Parse("Notes/edit.md"), BaselineConflictType.UnexpectedContent, "The target file content differs from the baseline.")]));
        MainWindowViewModel shell = new(
            new SequencedFolderPicker("D:/Vault/target"),
            new ImmediateCompareService(CreateResult()),
            new PatchUiStoragePicker("D:/Transfer/update.zip", "D:/Transactions/op/journal.json"),
            workflow);
        shell.NavigateCommand.Execute("Patches");
        await shell.Patches.OpenPatchAsync();
        await shell.Patches.SelectTargetAsync();
        await shell.Patches.ValidateAsync();
        MainWindow window = new(shell)
        {
            Width = 1280,
            Height = 780,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.FindControl<Border>("ConflictPanel")!.IsVisible);
        Assert.False(window.FindControl<Button>("ApplyPatchButton")!.IsEffectivelyEnabled);
        SaveFrame(window, Path.Combine(Path.GetTempPath(), "vaultdelta-ui-preview", "patch-conflict.png"));

        await shell.Patches.OpenJournalAsync();
        Assert.True(shell.Patches.CanRollback);
        window.Close();
    }

    private static void SaveFrame(MainWindow window, string path)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Bitmap frame = window.GetLastRenderedFrame()
            ?? throw new InvalidOperationException("The headless renderer did not produce a frame.");
        Assert.Equal(new Avalonia.PixelSize(1280, 780), frame.PixelSize);
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }

    private static CompareResult CreateResult()
    {
        SnapshotInventory baseline = SnapshotInventory.Create("rules", [File("Notes/old.md", 30, 'a'), File("Notes/edit.md", 100, 'b'), File("Archive/remove.md", 50, 'c')]);
        SnapshotInventory target = SnapshotInventory.Create("rules", [File("Notes/new.md", 30, 'a'), File("Notes/edit.md", 2048, 'd'), File(".obsidian/plugins/calendar/main.js", 4096, 'e')]);
        DiffSet differences = DiffEngine.Compare(baseline, target);
        CompareSummary summary = new(1, 1, 1, 1, 0, 2, 6144, 3);
        return new CompareResult(baseline, target, differences, summary);
    }

    private static SnapshotEntry File(string path, long length, char hash) =>
        new(RelativePath.Parse(path), SnapshotEntryKind.File, new FileFingerprint(length, DateTimeOffset.UnixEpoch, new string(hash, 64)));

    private static PackageInspectionResult CreateInspection()
    {
        PatchOperation operation = PatchOperation.Add(10, RelativePath.Parse("new.md"), new FileFingerprint(4096, DateTimeOffset.UnixEpoch, new string('f', 64)));
        PatchManifest manifest = new(
            "1.0",
            "vault-delta-preview",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            "rules",
            "base",
            "target",
            new PatchSummary(1, 0, 0, 0, 4096),
            [operation]);
        return new PackageInspectionResult(manifest, 1, 4096);
    }

    private sealed class NullFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class ImmediateCompareService(CompareResult result) : ICompareService
    {
        public ValueTask<CompareResult> CompareAsync(string baselinePath, string targetPath, IProgress<CompareProgress>? progress = null, CancellationToken cancellationToken = default) => ValueTask.FromResult(result);
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

    private sealed class ThrowingCompareService : ICompareService
    {
        public ValueTask<CompareResult> CompareAsync(string baselinePath, string targetPath, IProgress<CompareProgress>? progress = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<CompareResult>(new DirectoryNotFoundException());
    }

    private sealed class SequencedFolderPicker(params string[] paths) : IFolderPicker
    {
        private readonly Queue<string> _paths = new(paths);
        public Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(_paths.Count == 0 ? null : _paths.Dequeue());
    }

    private sealed class PatchUiStoragePicker(string patch, string journal) : IPatchStoragePicker
    {
        public Task<string?> SavePatchAsync(string suggestedFileName, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> OpenPatchAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(patch);
        public Task<string?> OpenJournalAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(journal);
    }

    private sealed class BuildStoragePicker(string output) : IPatchStoragePicker
    {
        public Task<string?> SavePatchAsync(string suggestedFileName, CancellationToken cancellationToken = default) => Task.FromResult<string?>(output);
        public Task<string?> OpenPatchAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> OpenJournalAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class ProgressPatchWorkflow : IPatchWorkflowService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask BuildAsync(CompareResult comparison, string sourceRoot, string outputPath, IProgress<PackageBuildProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            progress?.Report(new PackageBuildProgress(PackageBuildStage.Compressing, 72, 2, 5, "files/Notes/edit.md"));
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            progress?.Report(new PackageBuildProgress(PackageBuildStage.Completed, 100, 5, 5, "delta.zip"));
        }

        public ValueTask<PackageInspectionResult> InspectAsync(string packagePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BaselineValidationResult> ValidateAsync(PackageInspectionResult inspection, string targetRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ApplyResult> ApplyAsync(string packagePath, string targetRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RollbackResult> RollbackAsync(string journalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PatchUiWorkflow(PackageInspectionResult inspection, BaselineValidationResult validation) : IPatchWorkflowService
    {
        public ValueTask BuildAsync(CompareResult comparison, string sourceRoot, string outputPath, IProgress<PackageBuildProgress>? progress = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<PackageInspectionResult> InspectAsync(string packagePath, CancellationToken cancellationToken = default) => ValueTask.FromResult(inspection);
        public ValueTask<BaselineValidationResult> ValidateAsync(PackageInspectionResult packageInspection, string targetRoot, CancellationToken cancellationToken = default) => ValueTask.FromResult(validation);
        public ValueTask<ApplyResult> ApplyAsync(string packagePath, string targetRoot, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ApplyResult(ApplyJournalStatus.Committed, validation, "journal.json", null));
        public ValueTask<RollbackResult> RollbackAsync(string journalPath, CancellationToken cancellationToken = default) => ValueTask.FromResult(new RollbackResult(ApplyJournalStatus.RolledBack, journalPath, null));
    }
}
