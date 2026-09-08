using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Compare;

public sealed record CompareResult(
    SnapshotInventory Baseline,
    SnapshotInventory Target,
    DiffSet Differences,
    CompareSummary Summary);
