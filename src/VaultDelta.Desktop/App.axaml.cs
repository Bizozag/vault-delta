using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VaultDelta.Application.Compare;
using VaultDelta.Application.Snapshots;
using VaultDelta.Desktop.Services;
using VaultDelta.Desktop.ViewModels;
using VaultDelta.Infrastructure.FileSystem;
using VaultDelta.Infrastructure.Hashing;
using VaultDelta.Infrastructure.Presets;

namespace VaultDelta.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow? window = null;
            LocalFileSystem fileSystem = new();
            SnapshotScanner scanner = new(fileSystem, new Sha256ContentHasher());
            LocalCompareService compareService = new(
                new CompareWorkflow(scanner),
                ObsidianPresetProvider.LoadDefault());
            AvaloniaFolderPicker folderPicker = new(() => window);
            window = new MainWindow(new MainWindowViewModel(folderPicker, compareService));
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
