using System.IO.Compression;
using VaultDelta.Application.Abstractions;
using VaultDelta.Application.Patches;
using VaultDelta.Domain.Patches;

namespace VaultDelta.Infrastructure.Patches;

public sealed class ZipPackageWriter(
    DirectoryPackageWriter directoryWriter,
    PackageInspector zipInspector) : IPatchPackageWriter
{
    private readonly DirectoryPackageWriter _directoryWriter =
        directoryWriter ?? throw new ArgumentNullException(nameof(directoryWriter));
    private readonly PackageInspector _zipInspector =
        zipInspector ?? throw new ArgumentNullException(nameof(zipInspector));

    public async ValueTask WriteAsync(
        PatchManifest manifest,
        string sourceRoot,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string canonicalOutput = Path.GetFullPath(outputPath);
        if (File.Exists(canonicalOutput) || Directory.Exists(canonicalOutput))
        {
            throw new IOException($"Patch output already exists: {canonicalOutput}");
        }

        string? outputParent = Path.GetDirectoryName(canonicalOutput);
        if (string.IsNullOrEmpty(outputParent))
        {
            throw new ArgumentException("Patch output must have a parent directory.", nameof(outputPath));
        }

        Directory.CreateDirectory(outputParent);
        string operationId = Guid.NewGuid().ToString("N");
        string stagingDirectory = Path.Combine(outputParent, $".{Path.GetFileName(canonicalOutput)}.directory.incomplete-{operationId}");
        string stagingZip = Path.Combine(outputParent, $".{Path.GetFileName(canonicalOutput)}.incomplete-{operationId}");

        try
        {
            await _directoryWriter
                .WriteAsync(manifest, sourceRoot, stagingDirectory, cancellationToken)
                .ConfigureAwait(false);
            ZipFile.CreateFromDirectory(stagingDirectory, stagingZip, CompressionLevel.Optimal, includeBaseDirectory: false);
            await _zipInspector.InspectAsync(stagingZip, cancellationToken).ConfigureAwait(false);
            File.Move(stagingZip, canonicalOutput);
        }
        catch
        {
            if (File.Exists(stagingZip))
            {
                File.Delete(stagingZip);
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }
}
