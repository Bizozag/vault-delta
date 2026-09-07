using VaultDelta.Domain.Patches;

namespace VaultDelta.Application.Patches;

public sealed record PackageInspectionResult(
    PatchManifest Manifest,
    int VerifiedPayloadCount,
    long VerifiedPayloadBytes);
