namespace VaultDelta.Application.Snapshots;

public sealed class SnapshotScanException : Exception
{
    public SnapshotScanException(string message)
        : base(message)
    {
    }

    public SnapshotScanException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SnapshotScanException(string message, string rootPath, string? relativePath, Exception? innerException = null)
        : base(message, innerException)
    {
        RootPath = rootPath;
        RelativePath = relativePath;
    }

    public string? RootPath { get; }
    public string? RelativePath { get; }
}
