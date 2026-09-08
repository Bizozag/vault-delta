using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace VaultDelta.Desktop.Services;

public sealed class AvaloniaFolderPicker(Func<TopLevel?> topLevelProvider) : IFolderPicker
{
    private readonly Func<TopLevel?> _topLevelProvider = topLevelProvider
        ?? throw new ArgumentNullException(nameof(topLevelProvider));

    public async Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TopLevel topLevel = _topLevelProvider()
            ?? throw new InvalidOperationException("The folder picker requires an active window.");
        IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
            });
        cancellationToken.ThrowIfCancellationRequested();
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
