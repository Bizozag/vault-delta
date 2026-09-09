namespace VaultDelta.Application.Patches;

public sealed record PackageBuildProgress(
    PackageBuildStage Stage,
    int Percentage,
    int ProcessedItems,
    int TotalItems,
    string? CurrentPath = null);
