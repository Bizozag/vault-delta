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
}
