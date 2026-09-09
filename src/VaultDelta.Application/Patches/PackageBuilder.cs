using VaultDelta.Application.Abstractions;
using VaultDelta.Domain.Patches;

namespace VaultDelta.Application.Patches;

public sealed class PackageBuilder(IPatchPackageWriter packageWriter)
{
    private readonly IPatchPackageWriter _packageWriter =
        packageWriter ?? throw new ArgumentNullException(nameof(packageWriter));

    public ValueTask BuildAsync(
        PatchManifest manifest,
        string sourceRoot,
        string outputPath,
        IProgress<PackageBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();

        return _packageWriter.WriteAsync(manifest, sourceRoot, outputPath, progress, cancellationToken);
    }
}
