using VaultDelta.Domain.Patches;

namespace VaultDelta.Domain.Apply;

public sealed record ApplyJournalOperation(
    PatchOperation Operation,
    ApplyOperationStatus Status,
    string? BackupRelativePath);
