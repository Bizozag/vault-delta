namespace VaultDelta.Application.Apply;

public enum ApplyFaultPoint
{
    AfterPreparedJournal = 1,
    AfterBackup = 2,
    AfterTargetMutation = 3,
    AfterOperationJournal = 4,
    BeforeFinalVerification = 5,
    AfterRollbackMutation = 6,
}
