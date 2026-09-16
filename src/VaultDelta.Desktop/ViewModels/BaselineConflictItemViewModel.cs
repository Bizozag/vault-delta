using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VaultDelta.Application.Apply;

namespace VaultDelta.Desktop.ViewModels;

public sealed class BaselineConflictItemViewModel : INotifyPropertyChanged
{
    private readonly BaselineConflict _conflict;
    private readonly Action _selectionChanged;
    private readonly Func<bool> _canChoose;
    private readonly RelayCommand _ignoreCommand;
    private readonly RelayCommand _revertCommand;
    private ConflictResolutionAction? _selection;

    private BaselineConflictItemViewModel(
        BaselineConflict conflict,
        bool canRevert,
        Action selectionChanged,
        Func<bool> canChoose)
    {
        _conflict = conflict;
        _selectionChanged = selectionChanged;
        _canChoose = canChoose;
        CanRevert = canRevert;
        _ignoreCommand = new RelayCommand(_ => Select(ConflictResolutionAction.Ignore), _ => _canChoose());
        _revertCommand = new RelayCommand(_ => Select(ConflictResolutionAction.Revert), _ => _canChoose() && CanRevert);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Path => _conflict.Path.Value;
    public string Message => _conflict.Message;
    public string TypeText => _conflict.Type switch
    {
        BaselineConflictType.MissingExpected => "缺少基线项",
        BaselineConflictType.UnexpectedContent => "内容不一致",
        BaselineConflictType.UnexpectedType => "类型不一致",
        BaselineConflictType.UnexpectedExisting => "目标已存在",
        BaselineConflictType.Unreadable => "无法读取",
        _ => _conflict.Type.ToString(),
    };
    public bool CanRevert { get; }
    public bool IsResolved => _selection.HasValue;
    public string SelectionText => _selection switch
    {
        ConflictResolutionAction.Ignore => "已选 ignore：保留目标文件",
        ConflictResolutionAction.Revert => "已选 revert：采用更新包版本",
        _ => CanRevert ? "未处理" : "未处理：此类冲突仅支持 ignore",
    };
    public ICommand IgnoreCommand => _ignoreCommand;
    public ICommand RevertCommand => _revertCommand;
    public ConflictResolution? Resolution => _selection is { } action
        ? new ConflictResolution(_conflict.OperationSequence, _conflict.Path, _conflict.Type, _conflict.ActualFingerprint, action)
        : null;

    public static BaselineConflictItemViewModel Create(
        BaselineConflict conflict,
        bool canRevert,
        Action selectionChanged,
        Func<bool> canChoose) =>
        new(conflict, canRevert, selectionChanged, canChoose);

    public void NotifyCommands()
    {
        _ignoreCommand.NotifyCanExecuteChanged();
        _revertCommand.NotifyCanExecuteChanged();
    }

    private void Select(ConflictResolutionAction action)
    {
        if (!_canChoose() || (action == ConflictResolutionAction.Revert && !CanRevert))
        {
            return;
        }

        _selection = action;
        OnPropertyChanged(nameof(IsResolved));
        OnPropertyChanged(nameof(SelectionText));
        _selectionChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
