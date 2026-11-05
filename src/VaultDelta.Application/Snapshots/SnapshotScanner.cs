using VaultDelta.Application.Abstractions;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Rules;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Snapshots;

public sealed class SnapshotScanner(IFileSystem fileSystem, IContentHasher contentHasher)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IContentHasher _contentHasher = contentHasher ?? throw new ArgumentNullException(nameof(contentHasher));

    public async ValueTask<IReadOnlyList<string>> FindLinkedPathsAsync(
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

        List<string> links = [];
        string? currentPath = null;
        try
        {
            await foreach (FileSystemEntryMetadata metadata in _fileSystem.EnumerateEntriesAsync(
                rootPath, path => rules.Evaluate(RelativePath.Parse(path)).IsIncluded, cancellationToken).ConfigureAwait(false))
            {
                currentPath = metadata.RelativePath;
                RelativePath path = RelativePath.Parse(currentPath);
                if (metadata.IsLink && rules.Evaluate(path).IsIncluded)
                {
                    links.Add(path.Value);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new SnapshotScanException($"无法检查目录中的链接：{exception.Message}", rootPath, currentPath, exception);
        }

        return links.AsReadOnly();
    }

    public async ValueTask<SnapshotInventory> ScanAsync(
        string rootPath,
        SnapshotRuleSet rules,
        CancellationToken cancellationToken = default)
        => await ScanAsync(rootPath, rules, progress: null, cancellationToken).ConfigureAwait(false);

    public async ValueTask<SnapshotInventory> ScanAsync(
        string rootPath,
        SnapshotRuleSet rules,
        IProgress<SnapshotScanProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(rules);

        if (!_fileSystem.DirectoryExists(rootPath))
        {
            throw new DirectoryNotFoundException($"Snapshot root does not exist: {rootPath}");
        }

        List<SnapshotEntry> entries = [];
        string? currentPath = null;

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
                currentPath = metadata.RelativePath;
                RelativePath relativePath = RelativePath.Parse(metadata.RelativePath);
                if (!rules.Evaluate(relativePath).IsIncluded)
                {
                    continue;
                }

                if (metadata.IsLink)
                {
                    throw new SnapshotScanException(
                        "发现目录联接、符号链接或重解析点。可勾选“跳过链接”后重新比较；链接关系不会写入更新包。",
                        rootPath, relativePath.Value);
                }

                if (metadata.Type == FileSystemEntryType.Directory)
                {
                    entries.Add(new SnapshotEntry(relativePath, SnapshotEntryKind.Directory, null));
                    progress?.Report(new SnapshotScanProgress(entries.Count, relativePath.Value));
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
                    if (after.IsLink || after.Type != FileSystemEntryType.File)
                    {
                        throw new SnapshotScanException("文件在扫描期间变成了链接或目录，请停止写入后重试。", rootPath, relativePath.Value);
                    }
                    sha256 = await _contentHasher
                        .ComputeSha256Async(metadata.FullPath, cancellationToken)
                        .ConfigureAwait(false);
                    FileSystemEntryMetadata final = await _fileSystem
                        .GetEntryMetadataAsync(metadata.FullPath, metadata.RelativePath, cancellationToken)
                        .ConfigureAwait(false);

                    if (!IsStable(after, final))
                    {
                        throw new SnapshotScanException("文件在扫描期间持续变化，请停止写入后重试。", rootPath, relativePath.Value);
                    }

                    after = final;
                }

                entries.Add(
                    new SnapshotEntry(
                        relativePath,
                        SnapshotEntryKind.File,
                        new FileFingerprint(after.Length, after.LastWriteTimeUtc, sha256)));
                progress?.Report(new SnapshotScanProgress(entries.Count, relativePath.Value));
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new SnapshotScanException($"无法读取快照：{exception.Message}", rootPath, currentPath, exception);
        }

        return SnapshotInventory.Create(rules.RulesId, entries);
    }

    private static bool IsStable(FileSystemEntryMetadata before, FileSystemEntryMetadata after) =>
        before.Type == after.Type
        && before.Length == after.Length
        && before.LastWriteTimeUtc == after.LastWriteTimeUtc
        && before.IsLink == after.IsLink;
}
