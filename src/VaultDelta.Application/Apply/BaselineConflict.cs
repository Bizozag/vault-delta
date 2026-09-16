using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Apply;

public sealed record BaselineConflict(
    RelativePath Path,
    BaselineConflictType Type,
    string Message,
    int OperationSequence = 0,
    FileFingerprint? ActualFingerprint = null);
