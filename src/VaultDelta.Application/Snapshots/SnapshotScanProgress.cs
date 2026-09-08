namespace VaultDelta.Application.Snapshots;

public sealed record SnapshotScanProgress(int ProcessedEntries, string CurrentPath);
