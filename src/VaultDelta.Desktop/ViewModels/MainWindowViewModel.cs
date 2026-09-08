using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace VaultDelta.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private ShellPage _currentPage = ShellPage.Compare;

    public MainWindowViewModel()
    {
        NavigateCommand = new RelayCommand(Navigate);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand NavigateCommand { get; }

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
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageDescription));
        }
    }

    public bool IsCompareSelected => CurrentPage == ShellPage.Compare;

    public bool IsPatchesSelected => CurrentPage == ShellPage.Patches;

    public bool IsSettingsSelected => CurrentPage == ShellPage.Settings;

    public string PageTitle => CurrentPage switch
    {
        ShellPage.Compare => "创建增量补丁",
        ShellPage.Patches => "补丁与恢复",
        ShellPage.Settings => "设置",
        _ => throw new InvalidOperationException($"Unknown shell page: {CurrentPage}."),
    };

    public string PageDescription => CurrentPage switch
    {
        ShellPage.Compare => "比较两个 Obsidian 快照，只打包真正发生变化的内容。",
        ShellPage.Patches => "检查、应用离线补丁，并从未完成的事务中安全恢复。",
        ShellPage.Settings => "管理默认过滤范围、补丁格式和本机事务位置。",
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
