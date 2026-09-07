using VaultDelta.Application.Abstractions;
using VaultDelta.Domain.Apply;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Apply;

public sealed class RollbackWorkflow(
    IApplyJournalStore journalStore,
    IApplyLockManager lockManager,
    IApplyFileOperations fileOperations,
    ITargetStateReader targetStateReader,
    IApplyFaultInjector? faultInjector = null)
{
    private readonly IApplyJournalStore _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
    private readonly IApplyLockManager _lockManager = lockManager ?? throw new ArgumentNullException(nameof(lockManager));
    private readonly IApplyFileOperations _fileOperations = fileOperations ?? throw new ArgumentNullException(nameof(fileOperations));
    private readonly ITargetStateReader _targetStateReader = targetStateReader ?? throw new ArgumentNullException(nameof(targetStateReader));
    private readonly IApplyFaultInjector _faultInjector = faultInjector ?? NoOpApplyFaultInjector.Instance;

    public async ValueTask<RollbackResult> RollbackAsync(
        string journalPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        ApplyJournal journal = await _journalStore.LoadAsync(journalPath, cancellationToken).ConfigureAwait(false);
        if (journal.Status == ApplyJournalStatus.RolledBack)
        {
            return new RollbackResult(journal.Status, journalPath, null);
        }

        if (journal.Status is not (
            ApplyJournalStatus.Prepared
            or ApplyJournalStatus.Applying
            or ApplyJournalStatus.Verifying
            or ApplyJournalStatus.NeedsRollback
            or ApplyJournalStatus.Committed
            or ApplyJournalStatus.RollingBack))
        {
            throw new InvalidOperationException($"Journal status {journal.Status} cannot be rolled back.");
        }

        await using IAsyncDisposable targetLock = await _lockManager
            .AcquireAsync(journal.TargetRoot, journal.OperationId, cancellationToken)
            .ConfigureAwait(false);
        if (journal.Status != ApplyJournalStatus.RollingBack)
        {
            journal = journal.WithStatus(ApplyJournalStatus.RollingBack);
            await _journalStore.SaveAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            foreach (ApplyJournalOperation journalOperation in journal.Operations.Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (journalOperation.Status == ApplyOperationStatus.RolledBack)
                {
                    continue;
                }

                PatchOperation operation = journalOperation.Operation;
                if (journalOperation.Status == ApplyOperationStatus.Pending
                    || await IsRestoredAsync(operation, journal.TargetRoot, cancellationToken).ConfigureAwait(false))
                {
                    journal = journal.WithOperationStatus(operation.Sequence, ApplyOperationStatus.RolledBack);
                    await _journalStore.SaveAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                ReverseMutation(journalOperation, journal.TargetRoot, journal.BackupRoot);
                _faultInjector.ThrowIfRequested(ApplyFaultPoint.AfterRollbackMutation, operation.Sequence);
                await VerifyRestoredAsync(operation, journal.TargetRoot, cancellationToken).ConfigureAwait(false);
                journal = journal.WithOperationStatus(operation.Sequence, ApplyOperationStatus.RolledBack);
                await _journalStore.SaveAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
            }

            journal = journal.WithStatus(ApplyJournalStatus.RolledBack);
            await _journalStore.SaveAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
            return new RollbackResult(journal.Status, journalPath, null);
        }
        catch (Exception exception)
        {
            return new RollbackResult(journal.Status, journalPath, exception.Message);
        }
    }

    private void ReverseMutation(ApplyJournalOperation journalOperation, string targetRoot, string backupRoot)
    {
        PatchOperation operation = journalOperation.Operation;
        switch (operation.Type)
        {
            case PatchOperationType.Add:
                _fileOperations.RemoveAdded(targetRoot, operation.TargetPath!, operation.EntryKind);
                break;
            case PatchOperationType.Modify:
            case PatchOperationType.Delete:
                if (string.IsNullOrWhiteSpace(journalOperation.BackupRelativePath))
                {
                    throw new InvalidDataException($"Operation {operation.Sequence} is missing its backup path.");
                }

                _fileOperations.RestoreBackup(
                    backupRoot,
                    journalOperation.BackupRelativePath,
                    targetRoot,
                    operation.BasePath!,
                    operation.EntryKind);
                break;
            case PatchOperationType.Rename:
                _fileOperations.Move(targetRoot, operation.TargetPath!, operation.BasePath!);
                break;
            default:
                throw new NotSupportedException($"Unsupported patch operation: {operation.Type}/{operation.EntryKind}.");
        }
    }

    private async ValueTask<bool> IsRestoredAsync(
        PatchOperation operation,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        if (operation.Type == PatchOperationType.Add)
        {
            TargetEntryState state = await _targetStateReader
                .ReadAsync(targetRoot, operation.TargetPath!, cancellationToken)
                .ConfigureAwait(false);
            return state.Kind == TargetEntryStateKind.Missing;
        }

        TargetEntryState source = await _targetStateReader
            .ReadAsync(targetRoot, operation.BasePath!, cancellationToken)
            .ConfigureAwait(false);
        if (!MatchesOld(operation, source))
        {
            return false;
        }

        if (operation.Type != PatchOperationType.Rename)
        {
            return true;
        }

        TargetEntryState destination = await _targetStateReader
            .ReadAsync(targetRoot, operation.TargetPath!, cancellationToken)
            .ConfigureAwait(false);
        return destination.Kind == TargetEntryStateKind.Missing;
    }

    private async ValueTask VerifyRestoredAsync(
        PatchOperation operation,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        if (!await IsRestoredAsync(operation, targetRoot, cancellationToken).ConfigureAwait(false))
        {
            RelativePath path = operation.BasePath ?? operation.TargetPath!;
            throw new InvalidDataException($"Rollback verification failed: {path}.");
        }
    }

    private static bool MatchesOld(PatchOperation operation, TargetEntryState actual)
    {
        TargetEntryStateKind expected = operation.EntryKind == SnapshotEntryKind.File
            ? TargetEntryStateKind.File
            : TargetEntryStateKind.Directory;
        if (actual.Kind != expected)
        {
            return false;
        }

        return operation.EntryKind == SnapshotEntryKind.Directory
            || (actual.Fingerprint!.Length == operation.OldFingerprint!.Length
                && StringComparer.Ordinal.Equals(actual.Fingerprint.Sha256, operation.OldFingerprint.Sha256));
    }
}
