using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VaultDelta.Application.Compare;
using VaultDelta.Application.Snapshots;
using VaultDelta.Application.Apply;
using VaultDelta.Application.Patches;
using VaultDelta.Desktop.Services;
using VaultDelta.Desktop.ViewModels;
using VaultDelta.Infrastructure.FileSystem;
using VaultDelta.Infrastructure.Hashing;
using VaultDelta.Infrastructure.Apply;
using VaultDelta.Infrastructure.Patches;
using VaultDelta.Infrastructure.Platform;
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
            Sha256ContentHasher hasher = new();
            SnapshotScanner scanner = new(fileSystem, hasher);
            LocalCompareService compareService = new(
                new CompareWorkflow(scanner),
                ObsidianPresetProvider.LoadDefault());
            AvaloniaFolderPicker folderPicker = new(() => window);
            AvaloniaPatchStoragePicker storagePicker = new(() => window);
            PackageInspector inspector = new(new ZipPackageReader());
            DirectoryPackageWriter directoryWriter = new(hasher);
            PackageBuilder builder = new(new ZipPackageWriter(directoryWriter, inspector));
            LocalTargetStateReader stateReader = new(hasher);
            BaselineValidator baselineValidator = new(stateReader);
            LocalApplyFileOperations fileOperations = new();
            PlatformCapabilityProbe capabilityProbe = new();
            ApplyWorkflow applyWorkflow = new(
                inspector,
                baselineValidator,
                new PatchPayloadStager(hasher),
                new JsonApplyJournalStore(),
                new TargetLockManager(),
                new BackupStore(hasher),
                new AtomicFileWriter(hasher),
                fileOperations,
                stateReader,
                capabilityProbe);
            RollbackWorkflow rollbackWorkflow = new(
                new JsonApplyJournalStore(),
                new TargetLockManager(),
                fileOperations,
                stateReader,
                capabilityProbe);
            LocalPatchWorkflowService patchWorkflow = new(
                builder,
                inspector,
                baselineValidator,
                applyWorkflow,
                rollbackWorkflow);
            window = new MainWindow(new MainWindowViewModel(folderPicker, compareService, storagePicker, patchWorkflow));
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
