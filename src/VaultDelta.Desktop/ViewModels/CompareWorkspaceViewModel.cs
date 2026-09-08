using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VaultDelta.Application.Compare;
using VaultDelta.Desktop.Services;
using VaultDelta.Domain.Diffs;

namespace VaultDelta.Desktop.ViewModels;

public sealed class CompareWorkspaceViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IFolderPicker _folderPicker;
    private readonly ICompareService _compareService;
    private readonly AsyncRelayCommand _selectBaselineCommand;
    private readonly AsyncRelayCommand _selectTargetCommand;
    private readonly AsyncRelayCommand _compareCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _setFilterCommand;
    private string _baselinePath = string.Empty;
    private string _targetPath = string.Empty;
    private CompareSessionState _state;
    private CompareFilter _filter;
    private int _processedEntries;
    private string _currentPath = string.Empty;
    private string _errorMessage = string.Empty;
    private CompareResult? _result;
    private CancellationTokenSource? _comparisonCancellation;

    public CompareWorkspaceViewModel(IFolderPicker folderPicker, ICompareService compareService)
    {
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _compareService = compareService ?? throw new ArgumentNullException(nameof(compareService));
        _selectBaselineCommand = new AsyncRelayCommand(_ => SelectBaselineAsync(), _ => !IsBusy);
        _selectTargetCommand = new AsyncRelayCommand(_ => SelectTargetAsync(), _ => !IsBusy);
        _compareCommand = new AsyncRelayCommand(_ => CompareAsync(), _ => CanCompare);
        _cancelCommand = new RelayCommand(_ => Cancel(), _ => IsBusy);
        _setFilterCommand = new RelayCommand(SetFilter, _ => HasResult);
        SetState(CompareSessionState.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand SelectBaselineCommand => _selectBaselineCommand;
    public ICommand SelectTargetCommand => _selectTargetCommand;
    public ICommand CompareCommand => _compareCommand;
    public ICommand CancelCommand => _cancelCommand;
    public ICommand SetFilterCommand => _setFilterCommand;
    public ObservableCollection<DiffReviewItemViewModel> FilteredEntries { get; } = [];

    public string BaselinePath
    {
        get => _baselinePath;
        set => SetPath(ref _baselinePath, value, nameof(BaselinePath));
    }

    public string TargetPath
    {
        get => _targetPath;
        set => SetPath(ref _targetPath, value, nameof(TargetPath));
    }

    public CompareSessionState State => _state;
    public CompareFilter Filter => _filter;
    public bool IsBusy => State is CompareSessionState.ScanningBaseline or CompareSessionState.ScanningTarget or CompareSessionState.Comparing;
    public bool CanCompare => !IsBusy && !string.IsNullOrWhiteSpace(BaselinePath) && !string.IsNullOrWhiteSpace(TargetPath);
    public bool HasResult => _result is not null;
    public bool IsEmptyState => !HasResult && !IsBusy && State is not CompareSessionState.Error;
    public bool IsError => State == CompareSessionState.Error;
    public bool IsCancelled => State == CompareSessionState.Cancelled;
    public bool HasChanges => _result?.Summary.ChangedCount > 0;
    public bool HasNoChanges => HasResult && !HasChanges;
    public int ProcessedEntries => _processedEntries;
    public string CurrentPath => _currentPath;
    public string ErrorMessage => _errorMessage;
    public int AddedCount => _result?.Summary.AddedCount ?? 0;
    public int ModifiedCount => _result?.Summary.ModifiedCount ?? 0;
    public int DeletedCount => _result?.Summary.DeletedCount ?? 0;
    public int RenamedCount => _result?.Summary.RenamedCount ?? 0;
    public int RiskCount => _result?.Summary.RiskCount ?? 0;
    public string TransferSizeText => _result is null
        ? "—"
        : $"{_result.Summary.TransferFileCount} 个文件 · {DiffReviewItemViewModel.FormatBytes(_result.Summary.TransferBytes)}";
    public string StatusText => State switch
    {
        CompareSessionState.Empty => "请选择两个快照目录",
        CompareSessionState.Ready => "已就绪，可以开始只读扫描",
        CompareSessionState.ScanningBaseline => "正在扫描旧快照 / 基线",
        CompareSessionState.ScanningTarget => "正在扫描新快照 / 目标",
        CompareSessionState.Comparing => "正在分类差异",
        CompareSessionState.Completed when HasNoChanges => "比较完成，两个快照内容一致",
        CompareSessionState.Completed => "比较完成，请审核差异",
        CompareSessionState.Cancelled => "比较已取消，未保留半成品结果",
        CompareSessionState.Error => "比较未完成",
        _ => string.Empty,
    };

    public async Task SelectBaselineAsync()
    {
        string? selected = await _folderPicker.PickFolderAsync("选择旧快照 / 基线");
        if (!string.IsNullOrWhiteSpace(selected))
        {
            BaselinePath = selected;
        }
    }

    public async Task SelectTargetAsync()
    {
        string? selected = await _folderPicker.PickFolderAsync("选择新快照 / 目标");
        if (!string.IsNullOrWhiteSpace(selected))
        {
            TargetPath = selected;
        }
    }

    public async Task CompareAsync()
    {
        if (!CanCompare)
        {
            return;
        }

        _comparisonCancellation?.Dispose();
        _comparisonCancellation = new CancellationTokenSource();
        _result = null;
        _errorMessage = string.Empty;
        _processedEntries = 0;
        _currentPath = string.Empty;
        FilteredEntries.Clear();
        SetState(CompareSessionState.ScanningBaseline);
        NotifyResultProperties();

        try
        {
            Progress<CompareProgress> progress = new(UpdateProgress);
            _result = await _compareService.CompareAsync(
                BaselinePath,
                TargetPath,
                progress,
                _comparisonCancellation.Token);
            _filter = CompareFilter.All;
            RefreshEntries();
            SetState(CompareSessionState.Completed);
        }
        catch (OperationCanceledException) when (_comparisonCancellation.IsCancellationRequested)
        {
            SetState(CompareSessionState.Cancelled);
        }
        catch (Exception exception)
        {
            _errorMessage = ToUserMessage(exception);
            SetState(CompareSessionState.Error);
        }
        finally
        {
            NotifyResultProperties();
        }
    }

    public void Cancel() => _comparisonCancellation?.Cancel();

    public void Dispose()
    {
        _comparisonCancellation?.Dispose();
        _comparisonCancellation = null;
    }

    private void UpdateProgress(CompareProgress progress)
    {
        _processedEntries = progress.ProcessedEntries;
        _currentPath = progress.CurrentPath ?? string.Empty;
        CompareSessionState state = progress.Stage switch
        {
            CompareStage.ScanningBaseline => CompareSessionState.ScanningBaseline,
            CompareStage.ScanningTarget => CompareSessionState.ScanningTarget,
            CompareStage.Comparing => CompareSessionState.Comparing,
            CompareStage.Completed => _state,
            _ => _state,
        };
        SetState(state);
        OnPropertyChanged(nameof(ProcessedEntries));
        OnPropertyChanged(nameof(CurrentPath));
    }

    private void SetFilter(object? value)
    {
        if (value is string text && Enum.TryParse(text, true, out CompareFilter filter))
        {
            _filter = filter;
            OnPropertyChanged(nameof(Filter));
            RefreshEntries();
        }
    }

    private void RefreshEntries()
    {
        FilteredEntries.Clear();
        if (_result is null)
        {
            return;
        }

        IEnumerable<DiffReviewItemViewModel> items = _result.Differences.Entries
            .Where(entry => entry.Type != DiffEntryType.Unchanged)
            .Select(entry => DiffReviewItemViewModel.Create(entry, _result.Differences));
        items = _filter switch
        {
            CompareFilter.Added => items.Where(item => item.Type == DiffEntryType.Added),
            CompareFilter.Modified => items.Where(item => item.Type == DiffEntryType.Modified),
            CompareFilter.Deleted => items.Where(item => item.Type == DiffEntryType.Deleted),
            CompareFilter.Renamed => items.Where(item => item.Type == DiffEntryType.Renamed),
            CompareFilter.Risks => items.Where(item => item.IsRisk),
            _ => items,
        };
        foreach (DiffReviewItemViewModel item in items)
        {
            FilteredEntries.Add(item);
        }
    }

    private void SetPath(ref string field, string value, string propertyName)
    {
        value ??= string.Empty;
        if (StringComparer.Ordinal.Equals(field, value))
        {
            return;
        }

        field = value;
        _result = null;
        _errorMessage = string.Empty;
        FilteredEntries.Clear();
        SetState(CanSelectCompare() ? CompareSessionState.Ready : CompareSessionState.Empty);
        OnPropertyChanged(propertyName);
        NotifyResultProperties();
    }

    private bool CanSelectCompare() =>
        !string.IsNullOrWhiteSpace(_baselinePath) && !string.IsNullOrWhiteSpace(_targetPath);

    private void SetState(CompareSessionState state)
    {
        _state = state;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanCompare));
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(StatusText));
        _selectBaselineCommand?.NotifyCanExecuteChanged();
        _selectTargetCommand?.NotifyCanExecuteChanged();
        _compareCommand?.NotifyCanExecuteChanged();
        _cancelCommand?.NotifyCanExecuteChanged();
        _setFilterCommand?.NotifyCanExecuteChanged();
    }

    private void NotifyResultProperties()
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasNoChanges));
        OnPropertyChanged(nameof(AddedCount));
        OnPropertyChanged(nameof(ModifiedCount));
        OnPropertyChanged(nameof(DeletedCount));
        OnPropertyChanged(nameof(RenamedCount));
        OnPropertyChanged(nameof(RiskCount));
        OnPropertyChanged(nameof(TransferSizeText));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(StatusText));
    }

    private static string ToUserMessage(Exception exception) => exception switch
    {
        DirectoryNotFoundException => "所选目录不存在或已被移动，请重新选择。",
        UnauthorizedAccessException => "无法读取所选目录，请检查文件权限。",
        ArgumentException => exception.Message,
        _ => "无法安全完成比较。请检查目录、权限和正在写入的文件后重试。",
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
