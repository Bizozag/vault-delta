using VaultDelta.Domain.Rules;

namespace VaultDelta.Application.Compare;

public sealed record CompareRequest(
    string BaselinePath,
    string TargetPath,
    SnapshotRuleSet Rules);
