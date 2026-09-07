using VaultDelta.Application.Abstractions;
using VaultDelta.Application.Patches;
using VaultDelta.Domain.Apply;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Apply;

public sealed class ApplyWorkflow(
    PackageInspector packageInspector,
    BaselineValidator baselineValidator,
    IPatchPayloadStager payloadStager,
    IApplyJournalStore journalStore,
    IApplyLockManager lockManager,
    IBackupStore backupStore,
    IAtomicFileWriter atomicFileWriter,
    IApplyFileOperations fileOperations,
    ITargetStateReader targetStateReader,
    IApplyCapabilityValidator capabilityValidator,
    IApplyFaultInjector? faultInjector = null)
{
    private readonly PackageInspector _packageInspector = packageInspector ?? throw new ArgumentNullException(nameof(packageInspector));
    private readonly BaselineValidator _baselineValidator = baselineValidator ?? throw new ArgumentNullException(nameof(baselineValidator));
    private readonly IPatchPayloadStager _payloadStager = payloadStager ?? throw new ArgumentNullException(nameof(payloadStager));
    private readonly IApplyJournalStore _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
    private readonly IApplyLockManager _lockManager = lockManager ?? throw new ArgumentNullException(nameof(lockManager));
    private readonly IBackupStore _backupStore = backupStore ?? throw new ArgumentNullException(nameof(backupStore));
    private readonly IAtomicFileWriter _atomicFileWriter = atomicFileWriter ?? throw new ArgumentNullException(nameof(atomicFileWriter));
    private readonly IApplyFileOperations _fileOperations = fileOperations ?? throw new ArgumentNullException(nameof(fileOperations));
    private readonly ITargetStateReader _targetStateReader = targetStateReader ?? throw new ArgumentNullException(nameof(targetStateReader));
    private readonly IApplyCapabilityValidator _capabilityValidator = capabilityValidator ?? throw new ArgumentNullException(nameof(capabilityValidator));
    private readonly IApplyFaultInjector _faultInjector = faultInjector ?? NoOpApplyFaultInjector.Instance;

    public async ValueTask<ApplyResult> ApplyAsync(
        ApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TransactionRoot);

        PackageInspectionResult inspection = await _packageInspector
            .InspectAsync(request.PackagePath, cancellationToken)
            .ConfigureAwait(false);
        BaselineValidationResult baseline = await _baselineValidator
            .ValidateAsync(inspection.Manifest, request.TargetRoot, cancellationToken)
            .ConfigureAwait(false);
        if (baseline.Status == BaselineValidationStatus.Conflict)
        {
            return new ApplyResult(null, baseline, null, null);
        }

        await _capabilityValidator
            .ValidateAsync(request.TargetRoot, request.TransactionRoot, cancellationToken)
            .ConfigureAwait(false);

        string operationId = request.OperationId ?? Guid.NewGuid().ToString("N");
        ApplyTransactionPaths transaction = _fileOperations.CreateTransactionPaths(request.TransactionRoot, operationId);

        await using IAsyncDisposable targetLock = await _lockManager
            .AcquireAsync(request.TargetRoot, operationId, cancellationToken)
            .ConfigureAwait(false);
        await using IPatchPayloadSession payloads = await _payloadStager
            .StageAsync(request.PackagePath, inspection.Manifest, cancellationToken)
            .ConfigureAwait(false);

        ApplyJournal journal = ApplyJournal.Create(
            operationId,
            inspection.Manifest.PatchId,
            _fileOperations.GetCanonicalRoot(request.TargetRoot),
            transaction.BackupRoot,
            inspection.Manifest.Operations);
        await _journalStore.SaveAsync(transaction.JournalPath, journal, cancellationToken).ConfigureAwait(false);

        try
        {
            _faultInjector.ThrowIfRequested(ApplyFaultPoint.AfterPreparedJournal);
            journal = journal.WithStatus(ApplyJournalStatus.Applying);
            await _journalStore.SaveAsync(transaction.JournalPath, journal, cancellationToken).ConfigureAwait(false);

            foreach (PatchOperation operation in inspection.Manifest.Operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ValidateBeforeMutationAsync(operation, request.TargetRoot, cancellationToken).ConfigureAwait(false);

                string? backupRelativePath = operation.Type == PatchOperationType.Delete
                    ? _backupStore.GetDeletedBackupRelativePath(operation.BasePath!, operation.EntryKind)
                    : await PrepareAsync(
                        operation,
                        request.TargetRoot,
                        transaction.BackupRoot,
                        cancellationToken).ConfigureAwait(false);
                journal = journal.WithOperationStatus(
                    operation.Sequence,
                    ApplyOperationStatus.BackupCompleted,
                    backupRelativePath);
                await _journalStore.SaveAsync(transaction.JournalPath, journal, cancellationToken).ConfigureAwait(false);
                if (operation.Type == PatchOperationType.Delete)
                {
                    string actualBackupPath = _backupStore.MoveDeleted(
                        request.TargetRoot,
                        transaction.BackupRoot,
                        operation.BasePath!);
                    if (!StringComparer.Ordinal.Equals(actualBackupPath, backupRelativePath))
                    {
                        throw new InvalidDataException($"Delete backup path changed unexpectedly: {operation.BasePath}.");
                    }
                }

                _faultInjector.ThrowIfRequested(ApplyFaultPoint.AfterBackup, operation.Sequence);

                await MutateAsync(operation, request.TargetRoot, payloads, cancellationToken).ConfigureAwait(false);
                _faultInjector.ThrowIfRequested(ApplyFaultPoint.AfterTargetMutation, operation.Sequence);
                await VerifyAppliedAsync(operation, request.TargetRoot, cancellationToken).ConfigureAwait(false);

                journal = journal.WithOperationStatus(operation.Sequence, ApplyOperationStatus.Completed);
                await _journalStore.SaveAsync(transaction.JournalPath, journal, cancellationToken).ConfigureAwait(false);
                _faultInjector.ThrowIfRequested(ApplyFaultPoint.AfterOperationJournal, operation.Sequence);
            }

            journal = journal.WithStatus(ApplyJournalStatus.Verifying);
            await _journalStore.SaveAsync(transaction.JournalPath, journal, cancellationToken).ConfigureAwait(false);
            _faultInjector.ThrowIfRequested(ApplyFaultPoint.BeforeFinalVerification);
            foreach (PatchOperation operation in inspection.Manifest.Operations)
            {
                await VerifyAppliedAsync(operation, request.TargetRoot, cancellationToken).ConfigureAwait(false);
            }

            journal = journal.WithStatus(ApplyJournalStatus.Committed);
            await _journalStore.SaveAsync(transaction.JournalPath, journal, cancellationToken).ConfigureAwait(false);
            return new ApplyResult(journal.Status, baseline, transaction.JournalPath, null);
        }
        catch (Exception exception)
        {
            if (journal.Status is ApplyJournalStatus.Prepared or ApplyJournalStatus.Applying or ApplyJournalStatus.Verifying)
            {
                journal = journal.WithStatus(ApplyJournalStatus.NeedsRollback);
                await _journalStore.SaveAsync(transaction.JournalPath, journal, CancellationToken.None).ConfigureAwait(false);
            }

            return new ApplyResult(journal.Status, baseline, transaction.JournalPath, exception.Message);
        }
    }

    private async ValueTask<string?> PrepareAsync(
        PatchOperation operation,
        string targetRoot,
        string backupRoot,
        CancellationToken cancellationToken) =>
        operation.Type switch
        {
            PatchOperationType.Modify => await _backupStore
                .BackupFileAsync(targetRoot, backupRoot, operation.BasePath!, cancellationToken)
                .ConfigureAwait(false),
            _ => null,
        };

    private async ValueTask MutateAsync(
        PatchOperation operation,
        string targetRoot,
        IPatchPayloadSession payloads,
        CancellationToken cancellationToken)
    {
        switch (operation.Type)
        {
            case PatchOperationType.Add when operation.EntryKind == SnapshotEntryKind.Directory:
                _fileOperations.CreateDirectory(targetRoot, operation.TargetPath!);
                break;
            case PatchOperationType.Add:
            case PatchOperationType.Modify:
                await _atomicFileWriter.WriteVerifiedAsync(
                    payloads.GetPayloadPath(operation.PayloadPath!),
                    _fileOperations.GetTargetPath(targetRoot, operation.TargetPath!),
                    operation.NewFingerprint!,
                    cancellationToken).ConfigureAwait(false);
                break;
            case PatchOperationType.Delete:
                break;
            case PatchOperationType.Rename:
                _fileOperations.Move(targetRoot, operation.BasePath!, operation.TargetPath!);
                break;
            default:
                throw new NotSupportedException($"Unsupported patch operation: {operation.Type}/{operation.EntryKind}.");
        }
    }

    private async ValueTask ValidateBeforeMutationAsync(
        PatchOperation operation,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        if (operation.BasePath is not null)
        {
            TargetEntryState actual = await _targetStateReader
                .ReadAsync(targetRoot, operation.BasePath, cancellationToken)
                .ConfigureAwait(false);
            EnsureMatches(operation.BasePath, operation.EntryKind, operation.OldFingerprint, actual, expectedMissing: false);
        }

        if (operation.Type is PatchOperationType.Add or PatchOperationType.Rename)
        {
            TargetEntryState actual = await _targetStateReader
                .ReadAsync(targetRoot, operation.TargetPath!, cancellationToken)
                .ConfigureAwait(false);
            EnsureMatches(operation.TargetPath!, operation.EntryKind, null, actual, expectedMissing: true);
        }
    }

    private async ValueTask VerifyAppliedAsync(
        PatchOperation operation,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        RelativePath path = operation.Type == PatchOperationType.Delete
            ? operation.BasePath!
            : operation.TargetPath!;
        TargetEntryState actual = await _targetStateReader.ReadAsync(targetRoot, path, cancellationToken).ConfigureAwait(false);
        EnsureMatches(
            path,
            operation.EntryKind,
            operation.NewFingerprint,
            actual,
            expectedMissing: operation.Type == PatchOperationType.Delete);
    }

    internal static void EnsureMatches(
        RelativePath path,
        SnapshotEntryKind expectedKind,
        FileFingerprint? expectedFingerprint,
        TargetEntryState actual,
        bool expectedMissing)
    {
        if (expectedMissing)
        {
            if (actual.Kind != TargetEntryStateKind.Missing)
            {
                throw new InvalidDataException($"Expected {path} to be missing, but found {actual.Kind}.");
            }

            return;
        }

        TargetEntryStateKind expectedState = expectedKind == SnapshotEntryKind.File
            ? TargetEntryStateKind.File
            : TargetEntryStateKind.Directory;
        if (actual.Kind != expectedState)
        {
            throw new InvalidDataException($"Expected {path} to be {expectedState}, but found {actual.Kind}.");
        }

        if (expectedKind == SnapshotEntryKind.File
            && (actual.Fingerprint!.Length != expectedFingerprint!.Length
                || !StringComparer.Ordinal.Equals(actual.Fingerprint.Sha256, expectedFingerprint.Sha256)))
        {
            throw new InvalidDataException($"File content does not match the expected state: {path}.");
        }
    }
}
