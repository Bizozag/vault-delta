using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VaultDelta.Desktop.Services;

namespace VaultDelta.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private ShellPage _currentPage = ShellPage.Compare;
    private bool _isScopePanelExpanded;
    private readonly string _versionText = $"v{GetProductVersion()}";

    public MainWindowViewModel()
        : this(
            new PreviewFolderPicker(),
            new PreviewCompareService(),
            new PreviewPatchStoragePicker(),
            new PreviewPatchWorkflowService())
    {
    }

    public MainWindowViewModel(
        IFolderPicker folderPicker,
        ICompareService compareService,
        IPatchStoragePicker? patchStoragePicker = null,
        IPatchWorkflowService? patchWorkflow = null)
    {
        patchStoragePicker ??= new PreviewPatchStoragePicker();
        patchWorkflow ??= new PreviewPatchWorkflowService();
        Compare = new CompareWorkspaceViewModel(folderPicker, compareService, patchStoragePicker, patchWorkflow);
        Patches = new PatchWorkspaceViewModel(folderPicker, patchStoragePicker, patchWorkflow);
        NavigateCommand = new RelayCommand(Navigate);
        ToggleScopePanelCommand = new RelayCommand(_ => IsScopePanelExpanded = !IsScopePanelExpanded);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand NavigateCommand { get; }

    public ICommand ToggleScopePanelCommand { get; }

    public CompareWorkspaceViewModel Compare { get; }

    public PatchWorkspaceViewModel Patches { get; }

    public string VersionText => _versionText;

    public ShellPage CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (_currentPage == value)
            {
                return;
            }

            _currentPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCompareSelected));
            OnPropertyChanged(nameof(IsPatchesSelected));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageDescription));
            OnPropertyChanged(nameof(SwitchIndicatorOffset));
        }
    }

    public bool IsCompareSelected => CurrentPage == ShellPage.Compare;

    public bool IsPatchesSelected => CurrentPage == ShellPage.Patches;

    public double SwitchIndicatorOffset => IsPatchesSelected ? 176 : 0;

    public bool IsScopePanelExpanded
    {
        get => _isScopePanelExpanded;
        private set
        {
            if (_isScopePanelExpanded == value)
            {
                return;
            }

            _isScopePanelExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScopePanelButtonText));
        }
    }

    public string ScopePanelButtonText => IsScopePanelExpanded ? "收起设置" : "展开设置";

    public string PageTitle => CurrentPage switch
    {
        ShellPage.Compare => "创建更新包",
        ShellPage.Patches => "应用更新包",
        _ => throw new InvalidOperationException($"Unknown shell page: {CurrentPage}."),
    };

    public string PageDescription => CurrentPage switch
    {
        ShellPage.Compare => "比较两个 Obsidian 快照，把发生变化的内容整理成便携更新包。",
        ShellPage.Patches => "检查并应用离线更新包，遇到冲突时安全停止或恢复。",
        _ => throw new InvalidOperationException($"Unknown shell page: {CurrentPage}."),
    };

    private void Navigate(object? parameter)
    {
        if (parameter is ShellPage page)
        {
            CurrentPage = page;
            return;
        }

        if (parameter is string value && Enum.TryParse(value, ignoreCase: true, out ShellPage parsed))
        {
            CurrentPage = parsed;
            return;
        }

        throw new ArgumentException("Navigation requires a valid shell page.", nameof(parameter));
    }

    private static string GetProductVersion()
    {
        Version? version = typeof(MainWindowViewModel).Assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class PreviewFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class PreviewCompareService : ICompareService
    {
        public ValueTask<VaultDelta.Application.Compare.CompareResult> CompareAsync(
            string baselinePath,
            string targetPath,
            IProgress<VaultDelta.Application.Compare.CompareProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<VaultDelta.Application.Compare.CompareResult>(
                new InvalidOperationException("Preview mode cannot compare folders."));
    }

    private sealed class PreviewPatchStoragePicker : IPatchStoragePicker
    {
        public Task<string?> SavePatchAsync(string suggestedFileName, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> OpenPatchAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> OpenJournalAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class PreviewPatchWorkflowService : IPatchWorkflowService
    {
        private static InvalidOperationException Error() => new("Preview mode cannot run patch workflows.");

        public ValueTask BuildAsync(VaultDelta.Application.Compare.CompareResult comparison, string sourceRoot, string outputPath, IProgress<VaultDelta.Application.Patches.PackageBuildProgress>? progress = null, CancellationToken cancellationToken = default) => ValueTask.FromException(Error());
        public ValueTask<VaultDelta.Application.Patches.PackageInspectionResult> InspectAsync(string packagePath, CancellationToken cancellationToken = default) => ValueTask.FromException<VaultDelta.Application.Patches.PackageInspectionResult>(Error());
        public ValueTask<VaultDelta.Application.Apply.BaselineValidationResult> ValidateAsync(VaultDelta.Application.Patches.PackageInspectionResult inspection, string targetRoot, CancellationToken cancellationToken = default) => ValueTask.FromException<VaultDelta.Application.Apply.BaselineValidationResult>(Error());
        public ValueTask<VaultDelta.Application.Apply.ApplyResult> ApplyAsync(string packagePath, string targetRoot, CancellationToken cancellationToken = default) => ValueTask.FromException<VaultDelta.Application.Apply.ApplyResult>(Error());
        public ValueTask<VaultDelta.Application.Apply.RollbackResult> RollbackAsync(string journalPath, CancellationToken cancellationToken = default) => ValueTask.FromException<VaultDelta.Application.Apply.RollbackResult>(Error());
    }
}
