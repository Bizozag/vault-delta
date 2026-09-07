namespace VaultDelta.Infrastructure.Platform;

public sealed class MacOsFileSystemSemantics : IFileSystemSemantics
{
    public string PlatformName => "macOS";

    public bool IsCurrentPlatform => OperatingSystem.IsMacOS();

    public bool IsLink(FileSystemInfo entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Refresh();
        return entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }
}
