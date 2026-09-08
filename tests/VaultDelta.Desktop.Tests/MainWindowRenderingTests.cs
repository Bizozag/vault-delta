using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

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

    private static void SaveFrame(MainWindow window, string path)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Bitmap frame = window.GetLastRenderedFrame()
            ?? throw new InvalidOperationException("The headless renderer did not produce a frame.");
        Assert.Equal(new Avalonia.PixelSize(1280, 780), frame.PixelSize);
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }
}
