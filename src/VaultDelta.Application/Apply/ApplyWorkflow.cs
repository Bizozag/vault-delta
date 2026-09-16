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

    public ValueTask<ApplyResult> ApplyAsync(
        ApplyRequest request,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(request, null, cancellationToken);

    public async ValueTask<ApplyResult> ApplyAsync(
        ApplyRequest request,
        IProgress<TransactionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TransactionRoot);

        progress?.Report(new TransactionProgress(TransactionProgressStage.InspectingPackage, 0, 0, 0));
        PackageInspectionResult inspection = await _packageInspector
            .InspectAsync(request.PackagePath, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new TransactionProgress(TransactionProgressStage.ValidatingBaseline, 2, 0, inspection.Manifest.Operations.Count));
        BaselineValidationResult baseline = await _baselineValidator
            .ValidateAsync(inspection.Manifest, request.TargetRoot, cancellationToken)
            .ConfigureAwait(false);
        if (!TryResolveOperations(inspection.Manifest.Operations, baseline.Conflicts, request.Resolutions, out IReadOnlyList<PatchOperation> selectedOperations))
        {
            return new ApplyResult(null, baseline, null, null);
        }
        BaselineValidationResult acceptedBaseline = new([]);

        progress?.Report(new TransactionProgress(TransactionProgressStage.CheckingCapabilities, 5, 0, selectedOperations.Count));
        await _capabilityValidator
            .ValidateAsync(request.TargetRoot, request.TransactionRoot, cancellationToken)
            .ConfigureAwait(false);

        string operationId = request.OperationId ?? Guid.NewGuid().ToString("N");
        ApplyTransactionPaths transaction = _fileOperations.CreateTransactionPaths(request.TransactionRoot, operationId);
        IReadOnlyList<PatchOperation> plannedOperations = PatchOperationExecutionPlanner
            .Order(selectedOperations);

        await using IAsyncDisposable targetLock = await _lockManager
            .AcquireAsync(request.TargetRoot, operationId, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new TransactionProgress(TransactionProgressStage.StagingPayloads, 8, 0, plannedOperations.Count));
        await using IPatchPayloadSession payloads = await _payloadStager
            .StageAsync(request.PackagePath, inspection.Manifest, cancellationToken)
            .ConfigureAwait(false);

        ApplyJournal journal = ApplyJournal.Create(
            operationId,
            inspection.Manifest.PatchId,
            _fileOperations.GetCanonicalRoot(request.TargetRoot),
            transaction.BackupRoot,
            plannedOperations);
        await _journalStore.SaveAsync(transaction.JournalPath, journal, cancellationToken).ConfigureAwait(false);

        PatchOperation? currentOperation = null;
        string currentPhase = string.Empty;
        try
        {
            _faultInjector.ThrowIfRequested(ApplyFaultPoint.AfterPreparedJournal);
            journal = journal.WithStatus(ApplyJournalStatus.Applying);
            await _journalStore.SaveAsync(transaction.JournalPath, journal, cancellationToken).ConfigureAwait(false);

            progress?.Report(new TransactionProgress(TransactionProgressStage.Applying, 10, 0, plannedOperations.Count));
            int completed = 0;
            foreach (PatchOperation operation in plannedOperations)
            {
                currentOperation = operation;
                currentPhase = "检查目标状态";
                cancellationToken.ThrowIfCancellationRequested();
                await ValidateBeforeMutationAsync(operation, request.TargetRoot, cancellationToken).ConfigureAwait(false);

                currentPhase = "准备备份";
                string? backupRelativePath = operation.Type == PatchOperationType.Delete
                    ? _backupStore.GetDeletedBackupRelativePath(operation.BasePath!, operation.EntryKind)
                    : await PrepareAsync(
                        operation,
                        request.TargetRoot,
                        transaction.BackupRoot,
                        cancellationToken).ConfigureAwait(false);
                currentPhase = "记录备份状态";
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

                currentPhase = "写入目标文件";
                await MutateAsync(operation, request.TargetRoot, payloads, cancellationToken).ConfigureAwait(false);
                _faultInjector.ThrowIfRequested(ApplyFaultPoint.AfterTargetMutation, operation.Sequence);
                currentPhase = "校验目标文件";
                await VerifyAppliedAsync(operation, request.TargetRoot, cancellationToken).ConfigureAwait(false);

                currentPhase = "保存恢复记录";
                journal = journal.WithOperationStatus(operation.Sequence, ApplyOperationStatus.Completed);
                await _journalStore.SaveAsync(transaction.JournalPath, journal, cancellationToken).ConfigureAwait(false);
                _faultInjector.ThrowIfRequested(ApplyFaultPoint.AfterOperationJournal, operation.Sequence);
                completed++;
                progress?.Report(new TransactionProgress(
                    TransactionProgressStage.Applying,
                    10 + (int)(80L * completed / plannedOperations.Count),
                    completed,
                    plannedOperations.Count,
                    (operation.TargetPath ?? operation.BasePath)?.Value));
            }

            currentOperation = null;
            journal = journal.WithStatus(ApplyJournalStatus.Verifying);
            await _journalStore.SaveAsync(transaction.JournalPath, journal, cancellationToken).ConfigureAwait(false);
            _faultInjector.ThrowIfRequested(ApplyFaultPoint.BeforeFinalVerification);
            progress?.Report(new TransactionProgress(TransactionProgressStage.Verifying, 90, 0, plannedOperations.Count));
            int verified = 0;
            foreach (PatchOperation operation in plannedOperations)
            {
                currentOperation = operation;
                await VerifyAppliedAsync(operation, request.TargetRoot, cancellationToken).ConfigureAwait(false);
                verified++;
                progress?.Report(new TransactionProgress(
                    TransactionProgressStage.Verifying,
                    90 + (int)(9L * verified / plannedOperations.Count),
                    verified,
                    plannedOperations.Count,
                    (operation.TargetPath ?? operation.BasePath)?.Value));
            }

            currentOperation = null;
            journal = journal.WithStatus(ApplyJournalStatus.Committed);
            await _journalStore.SaveAsync(transaction.JournalPath, journal, cancellationToken).ConfigureAwait(false);
            progress?.Report(new TransactionProgress(TransactionProgressStage.Completed, 100, plannedOperations.Count, plannedOperations.Count));
            return new ApplyResult(journal.Status, acceptedBaseline, transaction.JournalPath, null);
        }
        catch (Exception exception)
        {
            string journalSaveError = string.Empty;
            if (journal.Status is ApplyJournalStatus.Prepared or ApplyJournalStatus.Applying or ApplyJournalStatus.Verifying)
            {
                journal = journal.WithStatus(ApplyJournalStatus.NeedsRollback);
                try
                {
                    await _journalStore.SaveAsync(transaction.JournalPath, journal, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception saveException) when (saveException is IOException or UnauthorizedAccessException)
                {
                    journalSaveError = $" 恢复记录状态写入失败：{saveException.Message}";
                }
            }

            string operationContext = currentOperation is null
                ? string.Empty
                : $"操作 {currentOperation.Sequence} ({currentOperation.Type}, {(currentOperation.TargetPath ?? currentOperation.BasePath)?.Value})，{currentPhase}时：";
            return new ApplyResult(journal.Status, acceptedBaseline, transaction.JournalPath, operationContext + exception.Message + journalSaveError);
        }
    }

    private static bool TryResolveOperations(
        IReadOnlyList<PatchOperation> operations,
        IReadOnlyList<BaselineConflict> conflicts,
        IReadOnlyList<ConflictResolution>? resolutions,
        out IReadOnlyList<PatchOperation> selectedOperations)
    {
        selectedOperations = [];
        resolutions ??= [];
        if (resolutions.Count != conflicts.Count)
        {
            return false;
        }

        HashSet<int> matched = [];
        foreach (BaselineConflict conflict in conflicts)
        {
            int index = -1;
            for (int candidate = 0; candidate < resolutions.Count; candidate++)
            {
                ConflictResolution resolution = resolutions[candidate];
                if (resolution.OperationSequence == conflict.OperationSequence
                    && resolution.Path == conflict.Path
                    && resolution.Type == conflict.Type
                    && resolution.ActualFingerprint == conflict.ActualFingerprint)
                {
                    index = candidate;
                    break;
                }
            }

            if (index < 0 || !matched.Add(index))
            {
                return false;
            }
        }

        Dictionary<int, ConflictResolutionAction> actions = [];
        foreach (ConflictResolution resolution in resolutions)
        {
            if (actions.TryGetValue(resolution.OperationSequence, out ConflictResolutionAction existing)
                && existing != resolution.Action)
            {
                return false;
            }

            actions[resolution.OperationSequence] = resolution.Action;
        }

        Dictionary<int, BaselineConflict[]> conflictsByOperation = conflicts
            .GroupBy(conflict => conflict.OperationSequence)
            .ToDictionary(group => group.Key, group => group.ToArray());
        List<PatchOperation> selected = [];
        foreach (PatchOperation operation in operations)
        {
            if (!actions.TryGetValue(operation.Sequence, out ConflictResolutionAction action))
            {
                selected.Add(operation);
                continue;
            }

            if (action == ConflictResolutionAction.Ignore)
            {
                continue;
            }

            BaselineConflict[] operationConflicts = conflictsByOperation[operation.Sequence];
            if (operationConflicts.Length != 1)
            {
                return false;
            }

            BaselineConflict conflict = operationConflicts[0];
            if (conflict.Type == BaselineConflictType.UnexpectedContent
                && conflict.ActualFingerprint is not null
                && operation.Type is PatchOperationType.Modify or PatchOperationType.Delete)
            {
                selected.Add(PatchOperation.Restore(
                    operation.Sequence,
                    operation.Type,
                    operation.EntryKind,
                    operation.BasePath,
                    operation.TargetPath,
                    operation.PayloadPath,
                    conflict.ActualFingerprint,
                    operation.NewFingerprint));
                continue;
            }

            return false;
        }

        selectedOperations = selected;
        return true;
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
