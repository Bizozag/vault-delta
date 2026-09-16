using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Apply;

public enum ConflictResolutionAction
{
    Ignore,
    Revert,
}

public sealed record ConflictResolution(
    int OperationSequence,
    RelativePath Path,
    BaselineConflictType Type,
    FileFingerprint? ActualFingerprint,
    ConflictResolutionAction Action);
