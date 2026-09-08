namespace VaultDelta.Desktop.Services;

public interface IFolderPicker
{
    Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default);
}
