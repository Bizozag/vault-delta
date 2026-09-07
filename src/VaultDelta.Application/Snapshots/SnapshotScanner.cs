using VaultDelta.Application.Abstractions;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Rules;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Snapshots;

public sealed class SnapshotScanner(IFileSystem fileSystem, IContentHasher contentHasher)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IContentHasher _contentHasher = contentHasher ?? throw new ArgumentNullException(nameof(contentHasher));

    public async ValueTask<SnapshotInventory> ScanAsync(
        string rootPath,
        SnapshotRuleSet rules,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(rules);

        if (!_fileSystem.DirectoryExists(rootPath))
        {
            throw new DirectoryNotFoundException($"Snapshot root does not exist: {rootPath}");
        }

        List<SnapshotEntry> entries = [];

        try
        {
            await foreach (FileSystemEntryMetadata metadata in
                _fileSystem
                    .EnumerateEntriesAsync(
                        rootPath,
                        relativePath => rules.Evaluate(RelativePath.Parse(relativePath)).IsIncluded,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                RelativePath relativePath = RelativePath.Parse(metadata.RelativePath);
                if (!rules.Evaluate(relativePath).IsIncluded)
                {
                    continue;
                }

                if (metadata.IsLink)
                {
                    throw new SnapshotScanException($"Links and special filesystem entries are not supported: {relativePath}");
                }

                if (metadata.Type == FileSystemEntryType.Directory)
                {
                    entries.Add(new SnapshotEntry(relativePath, SnapshotEntryKind.Directory, null));
                    continue;
                }

                FileSystemEntryMetadata before = metadata;
                string sha256 = await _contentHasher
                    .ComputeSha256Async(metadata.FullPath, cancellationToken)
                    .ConfigureAwait(false);
                FileSystemEntryMetadata after = await _fileSystem
                    .GetEntryMetadataAsync(metadata.FullPath, metadata.RelativePath, cancellationToken)
                    .ConfigureAwait(false);

                if (!IsStable(before, after))
                {
                    sha256 = await _contentHasher
                        .ComputeSha256Async(metadata.FullPath, cancellationToken)
                        .ConfigureAwait(false);
                    FileSystemEntryMetadata final = await _fileSystem
                        .GetEntryMetadataAsync(metadata.FullPath, metadata.RelativePath, cancellationToken)
                        .ConfigureAwait(false);

                    if (!IsStable(after, final))
                    {
                        throw new SnapshotScanException($"File changed repeatedly while it was scanned: {relativePath}");
                    }

                    after = final;
                }

                entries.Add(
                    new SnapshotEntry(
                        relativePath,
                        SnapshotEntryKind.File,
                        new FileFingerprint(after.Length, after.LastWriteTimeUtc, sha256)));
            }
        }
        catch (SnapshotScanException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SnapshotScanException("The snapshot could not be read safely.", exception);
        }

        return SnapshotInventory.Create(rules.RulesId, entries);
    }

    private static bool IsStable(FileSystemEntryMetadata before, FileSystemEntryMetadata after) =>
        before.Type == after.Type
        && before.Length == after.Length
        && before.LastWriteTimeUtc == after.LastWriteTimeUtc
        && before.IsLink == after.IsLink;
}
