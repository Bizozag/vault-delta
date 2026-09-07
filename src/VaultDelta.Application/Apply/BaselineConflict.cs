using VaultDelta.Domain.Paths;

namespace VaultDelta.Application.Apply;

public sealed record BaselineConflict(
    RelativePath Path,
    BaselineConflictType Type,
    string Message);
