using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using VaultDelta.Desktop.ViewModels;

namespace VaultDelta.Desktop;

public partial class MainWindow : Window
{
    private readonly Dictionary<Control, DropTargetKind> _dropTargets = [];

    public MainWindow()
        : this(new MainWindowViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        RegisterDropTarget("BaselineDropZone", DropTargetKind.BaselineFolder);
        RegisterDropTarget("TargetDropZone", DropTargetKind.TargetFolder);
        RegisterDropTarget("PatchPackageDropZone", DropTargetKind.PatchPackage);
        RegisterDropTarget("PatchTargetDropZone", DropTargetKind.PatchTargetFolder);
    }

    private void RegisterDropTarget(string controlName, DropTargetKind kind)
    {
        Control control = this.FindControl<Control>(controlName)
            ?? throw new InvalidOperationException($"Drop target was not found: {controlName}");
        _dropTargets.Add(control, kind);
        DragDrop.SetAllowDrop(control, true);
        DragDrop.AddDragOverHandler(control, OnDragOver);
        DragDrop.AddDropHandler(control, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs eventArgs)
    {
        eventArgs.DragEffects = TryGetAcceptedPath(sender, eventArgs.DataTransfer, out _, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (!TryGetAcceptedPath(sender, eventArgs.DataTransfer, out DropTargetKind kind, out string? path) ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        switch (kind)
        {
            case DropTargetKind.BaselineFolder:
                viewModel.Compare.SetDroppedBaselinePath(path);
                break;
            case DropTargetKind.TargetFolder:
                viewModel.Compare.SetDroppedTargetPath(path);
                break;
            case DropTargetKind.PatchPackage:
                await viewModel.Patches.OpenDroppedPatchAsync(path);
                break;
            case DropTargetKind.PatchTargetFolder:
                viewModel.Patches.SetDroppedTargetPath(path);
                break;
        }
    }

    private bool TryGetAcceptedPath(
        object? sender,
        IDataTransfer dataTransfer,
        out DropTargetKind kind,
        out string path)
    {
        kind = default;
        path = string.Empty;
        if (sender is not Control control || !_dropTargets.TryGetValue(control, out kind))
        {
            return false;
        }

        if (DataContext is MainWindowViewModel viewModel &&
            (kind is DropTargetKind.BaselineFolder or DropTargetKind.TargetFolder
                ? viewModel.Compare.IsBusy
                : viewModel.Patches.IsBusy))
        {
            return false;
        }

        IStorageItem[]? items = dataTransfer.TryGetFiles();
        if (items is not { Length: 1 })
        {
            return false;
        }

        IStorageItem item = items[0];
        bool typeMatches = kind switch
        {
            DropTargetKind.BaselineFolder or DropTargetKind.TargetFolder or DropTargetKind.PatchTargetFolder => item is IStorageFolder,
            DropTargetKind.PatchPackage => item is IStorageFile && string.Equals(Path.GetExtension(item.Name), ".zip", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
        if (!typeMatches || !item.Path.IsAbsoluteUri || !item.Path.IsFile)
        {
            return false;
        }

        path = item.Path.LocalPath;
        return !string.IsNullOrWhiteSpace(path);
    }

    private enum DropTargetKind
    {
        BaselineFolder,
        TargetFolder,
        PatchPackage,
        PatchTargetFolder,
    }
}
