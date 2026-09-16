namespace VaultDelta.Application.Apply;

public enum TransactionProgressStage
{
    InspectingPackage,
    ValidatingBaseline,
    CheckingCapabilities,
    StagingPayloads,
    Applying,
    Verifying,
    PreparingRecovery,
    Recovering,
    Completed,
}

public sealed record TransactionProgress(
    TransactionProgressStage Stage,
    int Percentage,
    int ProcessedOperations,
    int TotalOperations,
    string? CurrentPath = null);
