using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using VaultDelta.Desktop;

[assembly: AvaloniaTestApplication(typeof(VaultDelta.Desktop.Tests.DesktopTestApplication))]

namespace VaultDelta.Desktop.Tests;

public static class DesktopTestApplication
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
                ShouldRenderOnUIThread = true,
            });
}
