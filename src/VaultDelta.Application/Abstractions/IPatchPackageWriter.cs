using VaultDelta.Domain.Patches;

namespace VaultDelta.Application.Abstractions;

public interface IPatchPackageWriter
{
    ValueTask WriteAsync(
        PatchManifest manifest,
        string sourceRoot,
        string outputPath,
        CancellationToken cancellationToken = default);
}
