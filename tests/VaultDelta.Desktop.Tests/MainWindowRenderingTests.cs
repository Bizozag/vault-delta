using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using VaultDelta.Application.Compare;
using VaultDelta.Desktop.Services;
using VaultDelta.Desktop.ViewModels;
using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Desktop.Tests;

public sealed class MainWindowRenderingTests
{
    [AvaloniaFact]
    public void Default_shell_renders_compare_page_at_minimum_supported_size()
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
        Assert.False(window.FindControl<ScrollViewer>("SettingsPage")!.IsVisible);
        Assert.True(window.Bounds.Width >= 1024);
        Assert.True(window.Bounds.Height >= 680);
        Assert.NotNull(window.FindControl<Button>("CompareNavigation"));
        window.Close();
    }

    [AvaloniaFact]
    public void Navigation_buttons_switch_the_visible_page()
    {
        MainWindow window = new();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.FindControl<Button>("PatchesNavigation")!.Command!.Execute("Patches");
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.FindControl<ScrollViewer>("PatchesPage")!.IsVisible);
        Assert.False(window.FindControl<ScrollViewer>("ComparePage")!.IsVisible);

        window.FindControl<Button>("SettingsNavigation")!.Command!.Execute("Settings");
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.FindControl<ScrollViewer>("SettingsPage")!.IsVisible);
        Assert.False(window.FindControl<ScrollViewer>("PatchesPage")!.IsVisible);
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

        SaveFrame(window, Path.Combine(outputDirectory, "compare.png"));
        window.FindControl<Button>("PatchesNavigation")!.Command!.Execute("Patches");
        Dispatcher.UIThread.RunJobs();
        SaveFrame(window, Path.Combine(outputDirectory, "patches.png"));
        window.FindControl<Button>("SettingsNavigation")!.Command!.Execute("Settings");
        Dispatcher.UIThread.RunJobs();
        SaveFrame(window, Path.Combine(outputDirectory, "settings.png"));

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
}
