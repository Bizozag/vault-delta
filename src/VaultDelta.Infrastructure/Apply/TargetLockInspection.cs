namespace VaultDelta.Infrastructure.Apply;

public sealed record TargetLockInspection(
    TargetLockStatus Status,
    string? OperationId,
    int? ProcessId,
    DateTimeOffset? StartedAtUtc);
