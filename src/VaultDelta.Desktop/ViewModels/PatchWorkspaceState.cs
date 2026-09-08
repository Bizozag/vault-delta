namespace VaultDelta.Desktop.ViewModels;

public enum PatchWorkspaceState
{
    Empty,
    Inspecting,
    PackageReady,
    Validating,
    Conflict,
    ReadyToApply,
    Applying,
    Applied,
    NeedsRollback,
    RollingBack,
    RolledBack,
    Error,
}
