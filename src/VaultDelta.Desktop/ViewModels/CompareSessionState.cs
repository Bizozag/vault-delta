namespace VaultDelta.Desktop.ViewModels;

public enum CompareSessionState
{
    Empty,
    Ready,
    ScanningBaseline,
    ScanningTarget,
    Comparing,
    Completed,
    Cancelled,
    Error,
}
