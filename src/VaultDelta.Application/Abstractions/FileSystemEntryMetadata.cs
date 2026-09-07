namespace VaultDelta.Application.Abstractions;

public sealed record FileSystemEntryMetadata(
    string FullPath,
    string RelativePath,
    FileSystemEntryType Type,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    bool IsLink);
