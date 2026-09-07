namespace VaultDelta.Infrastructure.Platform;

public sealed class WindowsFileSystemSemantics : IFileSystemSemantics
{
    public string PlatformName => "Windows";

    public bool IsCurrentPlatform => OperatingSystem.IsWindows();

    public bool IsLink(FileSystemInfo entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Refresh();
        return entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }
}
