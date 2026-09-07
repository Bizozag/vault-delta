namespace VaultDelta.Application.Apply;

public sealed record ApplyRequest(
    string PackagePath,
    string TargetRoot,
    string TransactionRoot,
    string? OperationId = null);
