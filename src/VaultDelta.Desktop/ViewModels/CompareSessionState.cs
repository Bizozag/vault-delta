namespace VaultDelta.Desktop.ViewModels;

public enum CompareSessionState
{
    Empty,
    Ready,
    ScanningBaseline,
    ScanningTarget,
    Comparing,
    GeneratingPatch,
    Completed,
    Cancelled,
    Error,
}
