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
        IProgress<PackageBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        progress?.Report(new PackageBuildProgress(PackageBuildStage.Preparing, 0, 0, 0));

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
                .WriteAsync(
                    manifest,
                    sourceRoot,
                    stagingDirectory,
                    new InlineProgress<PackageBuildProgress>(item => progress?.Report(item with
                    {
                        Percentage = (int)Math.Round(item.Percentage * 0.6),
                    })),
                    cancellationToken)
                .ConfigureAwait(false);
            await CreateZipAsync(stagingDirectory, stagingZip, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new PackageBuildProgress(PackageBuildStage.Verifying, 92, 0, 0, Path.GetFileName(stagingZip)));
            await _zipInspector.InspectAsync(stagingZip, cancellationToken).ConfigureAwait(false);
            progress?.Report(new PackageBuildProgress(PackageBuildStage.Publishing, 98, 0, 0, Path.GetFileName(canonicalOutput)));
            File.Move(stagingZip, canonicalOutput);
            progress?.Report(new PackageBuildProgress(PackageBuildStage.Completed, 100, 0, 0, Path.GetFileName(canonicalOutput)));
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

    private static async ValueTask CreateZipAsync(
        string sourceDirectory,
        string destinationPath,
        IProgress<PackageBuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        string[] files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        await using FileStream destination = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(destination, ZipArchiveMode.Create, leaveOpen: false);
        for (int index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = files[index];
            string entryName = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            await using Stream entryStream = entry.Open();
            await using FileStream source = new(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
            int processed = index + 1;
            int percentage = 60 + (int)Math.Round(processed * 30d / Math.Max(files.Length, 1));
            progress?.Report(new PackageBuildProgress(
                PackageBuildStage.Compressing,
                percentage,
                processed,
                files.Length,
                entryName));
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
