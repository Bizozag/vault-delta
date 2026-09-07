using VaultDelta.Domain.Patches;

namespace VaultDelta.Application.Abstractions;

public sealed record PatchPackageContent(
    PatchManifest Manifest,
    IReadOnlyList<PatchPackageEntry> Entries);
