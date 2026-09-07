namespace VaultDelta.Domain.Apply;

public enum ApplyOperationStatus
{
    Pending = 1,
    BackupCompleted = 2,
    Completed = 3,
    RolledBack = 4,
}
