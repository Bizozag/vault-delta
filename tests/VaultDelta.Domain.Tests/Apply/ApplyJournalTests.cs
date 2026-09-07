using VaultDelta.Domain.Apply;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;

namespace VaultDelta.Domain.Tests.Apply;

public sealed class ApplyJournalTests
{
    [Fact]
    public void Create_initializes_a_prepared_journal()
    {
        ApplyJournal journal = ApplyJournal.Create(
            "operation-001",
            "patch-001",
            "C:/Vault",
            "C:/Backups/operation-001",
            [PatchOperation.AddDirectory(10, RelativePath.Parse("Folder"))]);

        Assert.Equal(ApplyJournalStatus.Prepared, journal.Status);
        Assert.Single(journal.Operations);
        Assert.Equal(ApplyOperationStatus.Pending, journal.Operations[0].Status);
    }

    [Fact]
    public void State_transitions_are_immutable_and_ordered()
    {
        ApplyJournal prepared = CreateJournal();
        ApplyJournal applying = prepared.WithStatus(ApplyJournalStatus.Applying);
        ApplyJournal backedUp = applying.WithOperationStatus(10, ApplyOperationStatus.BackupCompleted, "backup/Folder");
        ApplyJournal completed = backedUp.WithOperationStatus(10, ApplyOperationStatus.Completed, "backup/Folder");
        ApplyJournal verifying = completed.WithStatus(ApplyJournalStatus.Verifying);
        ApplyJournal committed = verifying.WithStatus(ApplyJournalStatus.Committed);

        Assert.Equal(ApplyJournalStatus.Prepared, prepared.Status);
        Assert.Equal(ApplyOperationStatus.Pending, prepared.Operations[0].Status);
        Assert.Equal(ApplyOperationStatus.Completed, completed.Operations[0].Status);
        Assert.Equal(ApplyJournalStatus.Committed, committed.Status);
        Assert.Throws<InvalidOperationException>(() => committed.WithStatus(ApplyJournalStatus.Applying));
    }

    [Fact]
    public void Failed_application_can_enter_and_complete_rollback()
    {
        ApplyJournal journal = CreateJournal()
            .WithStatus(ApplyJournalStatus.Applying)
            .WithStatus(ApplyJournalStatus.NeedsRollback)
            .WithStatus(ApplyJournalStatus.RollingBack)
            .WithOperationStatus(10, ApplyOperationStatus.RolledBack)
            .WithStatus(ApplyJournalStatus.RolledBack);

        Assert.Equal(ApplyJournalStatus.RolledBack, journal.Status);
        Assert.Equal(ApplyOperationStatus.RolledBack, journal.Operations[0].Status);
    }

    [Fact]
    public void Operation_status_cannot_skip_required_steps()
    {
        ApplyJournal journal = CreateJournal().WithStatus(ApplyJournalStatus.Applying);

        Assert.Throws<InvalidOperationException>(() =>
            journal.WithOperationStatus(10, ApplyOperationStatus.Completed));
    }

    private static ApplyJournal CreateJournal() =>
        ApplyJournal.Create(
            "operation-001",
            "patch-001",
            "C:/Vault",
            "C:/Backups/operation-001",
            [PatchOperation.AddDirectory(10, RelativePath.Parse("Folder"))]);
}
