namespace VaultDelta.Desktop.Services;

public interface IPatchStoragePicker
{
    Task<string?> SavePatchAsync(string suggestedFileName, CancellationToken cancellationToken = default);

    Task<string?> OpenPatchAsync(CancellationToken cancellationToken = default);

    Task<string?> OpenJournalAsync(CancellationToken cancellationToken = default);
}
