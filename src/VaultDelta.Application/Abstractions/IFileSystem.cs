namespace VaultDelta.Application.Abstractions;

public interface IFileSystem
{
    bool DirectoryExists(string path);

    IAsyncEnumerable<FileSystemEntryMetadata> EnumerateEntriesAsync(
        string rootPath,
        Func<string, bool>? shouldDescend = null,
        CancellationToken cancellationToken = default);

    ValueTask<FileSystemEntryMetadata> GetEntryMetadataAsync(
        string fullPath,
        string relativePath,
        CancellationToken cancellationToken = default);
}
