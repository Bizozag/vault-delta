namespace VaultDelta.Domain.Patches;

public sealed record PatchSummary(
    int Added,
    int Modified,
    int Deleted,
    int Renamed,
    long PayloadBytes);
