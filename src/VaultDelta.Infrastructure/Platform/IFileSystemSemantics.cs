namespace VaultDelta.Infrastructure.Platform;

public interface IFileSystemSemantics
{
    string PlatformName { get; }

    bool IsCurrentPlatform { get; }

    bool IsLink(FileSystemInfo entry);
}
