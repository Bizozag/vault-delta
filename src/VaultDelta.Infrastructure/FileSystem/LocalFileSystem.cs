using System.Runtime.CompilerServices;
using VaultDelta.Application.Abstractions;

namespace VaultDelta.Infrastructure.FileSystem;

public sealed class LocalFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public async IAsyncEnumerable<FileSystemEntryMetadata> EnumerateEntriesAsync(
        string rootPath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        string canonicalRoot = Path.GetFullPath(rootPath);
        Stack<string> pendingDirectories = new();
        pendingDirectories.Push(canonicalRoot);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string currentDirectory = pendingDirectories.Pop();

            foreach (string entryPath in Directory.EnumerateFileSystemEntries(currentDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileSystemEntryMetadata metadata = ReadMetadata(canonicalRoot, entryPath);
                yield return metadata;

                if (metadata.Type == FileSystemEntryType.Directory && !metadata.IsLink)
                {
                    pendingDirectories.Push(entryPath);
                }

                await Task.Yield();
            }
        }
    }

    public ValueTask<FileSystemEntryMetadata> GetEntryMetadataAsync(
        string fullPath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileSystemInfo info = CreateFileSystemInfo(fullPath);
        info.Refresh();
        return ValueTask.FromResult(ReadMetadata(info, relativePath));
    }

    private static FileSystemEntryMetadata ReadMetadata(string rootPath, string fullPath)
    {
        string relativePath = Path.GetRelativePath(rootPath, fullPath).Replace('\\', '/');
        return ReadMetadata(CreateFileSystemInfo(fullPath), relativePath);
    }

    private static FileSystemEntryMetadata ReadMetadata(FileSystemInfo info, string relativePath)
    {
        bool isDirectory = info.Attributes.HasFlag(FileAttributes.Directory);
        bool isLink = info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        long length = isDirectory ? 0 : ((FileInfo)info).Length;

        return new FileSystemEntryMetadata(
            info.FullName,
            relativePath,
            isDirectory ? FileSystemEntryType.Directory : FileSystemEntryType.File,
            length,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            isLink);
    }

    private static FileSystemInfo CreateFileSystemInfo(string path) =>
        Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
}
