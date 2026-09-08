using Avalonia.Controls;
using VaultDelta.Desktop.ViewModels;

namespace VaultDelta.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}
