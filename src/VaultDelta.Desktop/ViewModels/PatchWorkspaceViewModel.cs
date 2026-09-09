using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VaultDelta.Application.Apply;
using VaultDelta.Application.Patches;
using VaultDelta.Desktop.Services;

namespace VaultDelta.Desktop.ViewModels;

public sealed class PatchWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly IFolderPicker _folderPicker;
    private readonly IPatchStoragePicker _storagePicker;
    private readonly IPatchWorkflowService _workflow;
    private readonly AsyncRelayCommand _openPatchCommand;
    private readonly AsyncRelayCommand _selectTargetCommand;
    private readonly AsyncRelayCommand _validateCommand;
    private readonly AsyncRelayCommand _applyCommand;
    private readonly AsyncRelayCommand _openJournalCommand;
    private readonly AsyncRelayCommand _rollbackCommand;
    private string _packagePath = string.Empty;
    private string _targetPath = string.Empty;
    private string _journalPath = string.Empty;
    private string _errorMessage = string.Empty;
    private PatchWorkspaceState _state;
    private PackageInspectionResult? _inspection;

    public PatchWorkspaceViewModel(
        IFolderPicker folderPicker,
        IPatchStoragePicker storagePicker,
        IPatchWorkflowService workflow)
    {
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _storagePicker = storagePicker ?? throw new ArgumentNullException(nameof(storagePicker));
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _openPatchCommand = new AsyncRelayCommand(_ => OpenPatchAsync(), _ => !IsBusy);
        _selectTargetCommand = new AsyncRelayCommand(_ => SelectTargetAsync(), _ => !IsBusy);
        _validateCommand = new AsyncRelayCommand(_ => ValidateAsync(), _ => CanValidate);
        _applyCommand = new AsyncRelayCommand(_ => ApplyAsync(), _ => CanApply);
        _openJournalCommand = new AsyncRelayCommand(_ => OpenJournalAsync(), _ => !IsBusy);
        _rollbackCommand = new AsyncRelayCommand(_ => RollbackAsync(), _ => CanRollback);
        SetState(PatchWorkspaceState.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand OpenPatchCommand => _openPatchCommand;
    public ICommand SelectTargetCommand => _selectTargetCommand;
    public ICommand ValidateCommand => _validateCommand;
    public ICommand ApplyCommand => _applyCommand;
    public ICommand OpenJournalCommand => _openJournalCommand;
    public ICommand RollbackCommand => _rollbackCommand;
    public ObservableCollection<BaselineConflictItemViewModel> Conflicts { get; } = [];

    public string PackagePath => _packagePath;
    public string TargetPath => _targetPath;
    public string JournalPath => _journalPath;
    public string ErrorMessage => _errorMessage;
    public PatchWorkspaceState State => _state;
    public bool IsBusy => State is PatchWorkspaceState.Inspecting or PatchWorkspaceState.Validating or PatchWorkspaceState.Applying or PatchWorkspaceState.RollingBack;
    public bool HasPackage => _inspection is not null;
    public bool HasConflicts => Conflicts.Count > 0;
    public bool CanValidate => !IsBusy && HasPackage && !string.IsNullOrWhiteSpace(TargetPath);
    public bool CanApply => !IsBusy && State == PatchWorkspaceState.ReadyToApply;
    public bool CanRollback => !IsBusy && !string.IsNullOrWhiteSpace(JournalPath);
    public bool ShowPackageSummary => HasPackage;
    public bool ShowConflictPanel => State == PatchWorkspaceState.Conflict;
    public bool ShowApplyResult => State == PatchWorkspaceState.Applied;
    public bool ShowNeedsRollback => State == PatchWorkspaceState.NeedsRollback;
    public bool ShowRollbackResult => State == PatchWorkspaceState.RolledBack;
    public bool IsError => State == PatchWorkspaceState.Error;
    public string PatchId => _inspection?.Manifest.PatchId ?? "—";
    public int OperationCount => _inspection?.Manifest.Operations.Count ?? 0;
    public string PayloadText => _inspection is null
        ? "—"
        : $"{_inspection.VerifiedPayloadCount} 个文件 · {DiffReviewItemViewModel.FormatBytes(_inspection.VerifiedPayloadBytes)}";
    public string SummaryText => _inspection is null
        ? "尚未打开补丁"
        : $"新增 {_inspection.Manifest.Summary.Added} · 修改 {_inspection.Manifest.Summary.Modified} · 删除 {_inspection.Manifest.Summary.Deleted} · 重命名 {_inspection.Manifest.Summary.Renamed}";
    public string StatusText => State switch
    {
        PatchWorkspaceState.Empty => "打开补丁后先进行完整性检查",
        PatchWorkspaceState.Inspecting => "正在验证清单和载荷",
        PatchWorkspaceState.PackageReady => "补丁完整，等待选择目标库",
        PatchWorkspaceState.Validating => "正在检查目标基线",
        PatchWorkspaceState.Conflict => "发现基线冲突，尚未写入",
        PatchWorkspaceState.ReadyToApply => "Gate 已通过，可以安全应用",
        PatchWorkspaceState.Applying => "正在备份、写入并验证",
        PatchWorkspaceState.Applied => "补丁应用并验证完成",
        PatchWorkspaceState.NeedsRollback => "应用中断，需要恢复",
        PatchWorkspaceState.RollingBack => "正在恢复备份和原路径",
        PatchWorkspaceState.RolledBack => "恢复完成，目标已回到原状态",
        PatchWorkspaceState.Error => "操作未完成",
        _ => string.Empty,
    };

    public async Task OpenPatchAsync()
    {
        string? path = await _storagePicker.OpenPatchAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await InspectPatchAsync(path);
    }

    public async Task<bool> OpenDroppedPatchAsync(string path)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await InspectPatchAsync(fullPath);
        return HasPackage;
    }

    public bool SetDroppedTargetPath(string path)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
            {
                return false;
            }

            SetTargetPath(fullPath);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private async Task InspectPatchAsync(string path)
    {
        _packagePath = path;
        _inspection = null;
        _errorMessage = string.Empty;
        Conflicts.Clear();
        SetState(PatchWorkspaceState.Inspecting);
        OnPropertyChanged(nameof(PackagePath));
        try
        {
            _inspection = await _workflow.InspectAsync(path);
            SetState(PatchWorkspaceState.PackageReady);
        }
        catch (Exception exception)
        {
            _errorMessage = ToUserMessage(exception, "补丁包未通过完整性检查。");
            SetState(PatchWorkspaceState.Error);
        }
        finally
        {
            NotifySummary();
        }
    }

    public async Task SelectTargetAsync()
    {
        string? selected = await _folderPicker.PickFolderAsync("选择要更新的 Obsidian 库");
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        SetTargetPath(selected);
    }

    public async Task ValidateAsync()
    {
        if (!CanValidate || _inspection is null)
        {
            return;
        }

        Conflicts.Clear();
        _errorMessage = string.Empty;
        SetState(PatchWorkspaceState.Validating);
        try
        {
            BaselineValidationResult result = await _workflow.ValidateAsync(_inspection, TargetPath);
            foreach (BaselineConflict conflict in result.Conflicts)
            {
                Conflicts.Add(BaselineConflictItemViewModel.Create(conflict));
            }

            SetState(result.Status == BaselineValidationStatus.Pass
                ? PatchWorkspaceState.ReadyToApply
                : PatchWorkspaceState.Conflict);
        }
        catch (Exception exception)
        {
            _errorMessage = ToUserMessage(exception, "无法检查目标库基线。");
            SetState(PatchWorkspaceState.Error);
        }
    }

    public async Task ApplyAsync()
    {
        if (!CanApply)
        {
            return;
        }

        _errorMessage = string.Empty;
        SetState(PatchWorkspaceState.Applying);
        try
        {
            ApplyResult result = await _workflow.ApplyAsync(PackagePath, TargetPath);
            if (result.Baseline.Status == BaselineValidationStatus.Conflict)
            {
                Conflicts.Clear();
                foreach (BaselineConflict conflict in result.Baseline.Conflicts)
                {
                    Conflicts.Add(BaselineConflictItemViewModel.Create(conflict));
                }

                SetState(PatchWorkspaceState.Conflict);
                return;
            }

            _journalPath = result.JournalPath ?? string.Empty;
            OnPropertyChanged(nameof(JournalPath));
            if (result.Succeeded)
            {
                SetState(PatchWorkspaceState.Applied);
            }
            else if (result.RequiresRollback)
            {
                _errorMessage = result.Error ?? "应用未完成，请立即执行恢复。";
                SetState(PatchWorkspaceState.NeedsRollback);
            }
            else
            {
                _errorMessage = result.Error ?? "应用未完成。";
                SetState(PatchWorkspaceState.Error);
            }
        }
        catch (Exception exception)
        {
            _errorMessage = ToUserMessage(exception, "应用前的安全检查未通过。");
            SetState(PatchWorkspaceState.Error);
        }
    }

    public async Task OpenJournalAsync()
    {
        string? selected = await _storagePicker.OpenJournalAsync();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            _journalPath = selected;
            _errorMessage = string.Empty;
            OnPropertyChanged(nameof(JournalPath));
            NotifyCommands();
        }
    }

    public async Task RollbackAsync()
    {
        if (!CanRollback)
        {
            return;
        }

        _errorMessage = string.Empty;
        SetState(PatchWorkspaceState.RollingBack);
        try
        {
            RollbackResult result = await _workflow.RollbackAsync(JournalPath);
            if (result.Succeeded)
            {
                SetState(PatchWorkspaceState.RolledBack);
            }
            else
            {
                _errorMessage = result.Error ?? "恢复未完成，可使用同一 Journal 重试。";
                SetState(PatchWorkspaceState.Error);
            }
        }
        catch (Exception exception)
        {
            _errorMessage = ToUserMessage(exception, "无法读取或恢复该事务。可使用同一 Journal 重试。");
            SetState(PatchWorkspaceState.Error);
        }
    }

    private void SetState(PatchWorkspaceState state)
    {
        _state = state;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowConflictPanel));
        OnPropertyChanged(nameof(ShowApplyResult));
        OnPropertyChanged(nameof(ShowNeedsRollback));
        OnPropertyChanged(nameof(ShowRollbackResult));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(ErrorMessage));
        NotifyCommands();
    }

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(HasPackage));
        OnPropertyChanged(nameof(ShowPackageSummary));
        OnPropertyChanged(nameof(PatchId));
        OnPropertyChanged(nameof(OperationCount));
        OnPropertyChanged(nameof(PayloadText));
        OnPropertyChanged(nameof(SummaryText));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanValidate));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanRollback));
        _openPatchCommand?.NotifyCanExecuteChanged();
        _selectTargetCommand?.NotifyCanExecuteChanged();
        _validateCommand?.NotifyCanExecuteChanged();
        _applyCommand?.NotifyCanExecuteChanged();
        _openJournalCommand?.NotifyCanExecuteChanged();
        _rollbackCommand?.NotifyCanExecuteChanged();
    }

    private void SetTargetPath(string path)
    {
        _targetPath = path;
        Conflicts.Clear();
        if (HasPackage)
        {
            SetState(PatchWorkspaceState.PackageReady);
        }

        OnPropertyChanged(nameof(TargetPath));
        NotifyCommands();
    }

    private static string ToUserMessage(Exception exception, string fallback) => exception switch
    {
        DirectoryNotFoundException => "目录不存在或已被移动。",
        FileNotFoundException => "文件不存在或已被移动。",
        UnauthorizedAccessException => "没有足够权限读取或写入所选位置。",
        InvalidDataException => exception.Message,
        IOException => exception.Message,
        PlatformNotSupportedException => exception.Message,
        _ => fallback,
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
