namespace VaultDelta.Domain.Rules;

public sealed record RuleEvaluation(bool IsIncluded, SnapshotRule? MatchedRule);
