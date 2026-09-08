namespace VaultDelta.Application.Compare;

public sealed record CompareProgress(
    CompareStage Stage,
    int ProcessedEntries,
    string? CurrentPath = null);
