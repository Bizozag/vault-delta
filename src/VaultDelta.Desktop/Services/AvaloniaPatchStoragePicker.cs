using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace VaultDelta.Desktop.Services;

public sealed class AvaloniaPatchStoragePicker(Func<TopLevel?> topLevelProvider) : IPatchStoragePicker
{
    private static readonly FilePickerFileType ZipType = new("Vault Delta ZIP")
    {
        Patterns = ["*.zip"],
        MimeTypes = ["application/zip"],
    };

    private static readonly FilePickerFileType JournalType = new("Vault Delta Journal")
    {
        Patterns = ["journal.json"],
        MimeTypes = ["application/json"],
    };

    private readonly Func<TopLevel?> _topLevelProvider = topLevelProvider
        ?? throw new ArgumentNullException(nameof(topLevelProvider));

    public async Task<string?> SavePatchAsync(string suggestedFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IStorageFile? file = await GetTopLevel().StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "保存增量补丁",
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "zip",
                FileTypeChoices = [ZipType],
                ShowOverwritePrompt = true,
            });
        cancellationToken.ThrowIfCancellationRequested();
        return file?.TryGetLocalPath();
    }

    public Task<string?> OpenPatchAsync(CancellationToken cancellationToken = default) =>
        OpenSingleAsync("打开 Vault Delta 补丁", [ZipType], cancellationToken);

    public Task<string?> OpenJournalAsync(CancellationToken cancellationToken = default) =>
        OpenSingleAsync("打开恢复 Journal", [JournalType], cancellationToken);

    private async Task<string?> OpenSingleAsync(
        string title,
        IReadOnlyList<FilePickerFileType> types,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<IStorageFile> files = await GetTopLevel().StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = types,
            });
        cancellationToken.ThrowIfCancellationRequested();
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    private TopLevel GetTopLevel() => _topLevelProvider()
        ?? throw new InvalidOperationException("The file picker requires an active window.");
}
