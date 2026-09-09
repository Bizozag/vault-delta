using VaultDelta.Domain.Patches;
using VaultDelta.Application.Patches;

namespace VaultDelta.Application.Abstractions;

public interface IPatchPackageWriter
{
    ValueTask WriteAsync(
        PatchManifest manifest,
        string sourceRoot,
        string outputPath,
        IProgress<PackageBuildProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
