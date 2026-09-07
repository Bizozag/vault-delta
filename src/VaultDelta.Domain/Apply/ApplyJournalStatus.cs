namespace VaultDelta.Domain.Apply;

public enum ApplyJournalStatus
{
    Prepared = 1,
    Applying = 2,
    Verifying = 3,
    Committed = 4,
    NeedsRollback = 5,
    RollingBack = 6,
    RolledBack = 7,
}
