using System.Collections.ObjectModel;
using VaultDelta.Domain.Patches;

namespace VaultDelta.Domain.Apply;

public sealed class ApplyJournal
{
    public const string CurrentSchemaVersion = "1.0";

    public ApplyJournal(
        string schemaVersion,
        string operationId,
        string patchId,
        string targetRoot,
        string backupRoot,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        ApplyJournalStatus status,
        IReadOnlyList<ApplyJournalOperation> operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(patchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        ArgumentNullException.ThrowIfNull(operations);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown journal status.");
        }

        ValidateOperations(operations);
        SchemaVersion = schemaVersion;
        OperationId = operationId;
        PatchId = patchId;
        TargetRoot = targetRoot;
        BackupRoot = backupRoot;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
        Status = status;
        Operations = new ReadOnlyCollection<ApplyJournalOperation>(operations.ToArray());
    }

    public string SchemaVersion { get; }

    public string OperationId { get; }

    public string PatchId { get; }

    public string TargetRoot { get; }

    public string BackupRoot { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public ApplyJournalStatus Status { get; }

    public IReadOnlyList<ApplyJournalOperation> Operations { get; }

    public static ApplyJournal Create(
        string operationId,
        string patchId,
        string targetRoot,
        string backupRoot,
        IReadOnlyList<PatchOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ApplyJournal(
            CurrentSchemaVersion,
            operationId,
            patchId,
            targetRoot,
            backupRoot,
            now,
            now,
            ApplyJournalStatus.Prepared,
            operations.Select(operation =>
                new ApplyJournalOperation(operation, ApplyOperationStatus.Pending, null)).ToArray());
    }

    public ApplyJournal WithStatus(ApplyJournalStatus status)
    {
        if (!IsAllowedTransition(Status, status))
        {
            throw new InvalidOperationException($"Journal cannot transition from {Status} to {status}.");
        }

        return Copy(status, Operations);
    }

    public ApplyJournal WithOperationStatus(
        int sequence,
        ApplyOperationStatus status,
        string? backupRelativePath = null)
    {
        int index = Operations.ToList().FindIndex(item => item.Operation.Sequence == sequence);
        if (index < 0)
        {
            throw new ArgumentException($"Journal does not contain operation sequence {sequence}.", nameof(sequence));
        }

        ApplyJournalOperation current = Operations[index];
        if (!IsAllowedOperationTransition(current.Status, status))
        {
            throw new InvalidOperationException(
                $"Operation {sequence} cannot transition from {current.Status} to {status}.");
        }

        ApplyJournalOperation[] updated = Operations.ToArray();
        updated[index] = current with
        {
            Status = status,
            BackupRelativePath = backupRelativePath ?? current.BackupRelativePath,
        };
        return Copy(Status, updated);
    }

    private ApplyJournal Copy(ApplyJournalStatus status, IReadOnlyList<ApplyJournalOperation> operations) =>
        new(
            SchemaVersion,
            OperationId,
            PatchId,
            TargetRoot,
            BackupRoot,
            CreatedAtUtc,
            DateTimeOffset.UtcNow,
            status,
            operations);

    private static bool IsAllowedTransition(ApplyJournalStatus current, ApplyJournalStatus next) =>
        (current, next) switch
        {
            (ApplyJournalStatus.Prepared, ApplyJournalStatus.Applying) => true,
            (ApplyJournalStatus.Applying, ApplyJournalStatus.Verifying) => true,
            (ApplyJournalStatus.Applying, ApplyJournalStatus.NeedsRollback) => true,
            (ApplyJournalStatus.Verifying, ApplyJournalStatus.Committed) => true,
            (ApplyJournalStatus.Verifying, ApplyJournalStatus.NeedsRollback) => true,
            (ApplyJournalStatus.NeedsRollback, ApplyJournalStatus.RollingBack) => true,
            (ApplyJournalStatus.RollingBack, ApplyJournalStatus.RolledBack) => true,
            _ => false,
        };

    private static bool IsAllowedOperationTransition(ApplyOperationStatus current, ApplyOperationStatus next) =>
        (current, next) switch
        {
            (ApplyOperationStatus.Pending, ApplyOperationStatus.RolledBack) => true,
            (ApplyOperationStatus.Pending, ApplyOperationStatus.BackupCompleted) => true,
            (ApplyOperationStatus.BackupCompleted, ApplyOperationStatus.Completed) => true,
            (ApplyOperationStatus.BackupCompleted, ApplyOperationStatus.RolledBack) => true,
            (ApplyOperationStatus.Completed, ApplyOperationStatus.RolledBack) => true,
            _ => false,
        };

    private static void ValidateOperations(IReadOnlyList<ApplyJournalOperation> operations)
    {
        HashSet<int> sequences = [];
        foreach (ApplyJournalOperation operation in operations)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(operation.Operation);
            if (!Enum.IsDefined(operation.Status))
            {
                throw new ArgumentOutOfRangeException(nameof(operations), operation.Status, "Unknown operation status.");
            }

            if (!sequences.Add(operation.Operation.Sequence))
            {
                throw new ArgumentException(
                    $"Journal contains duplicate operation sequence {operation.Operation.Sequence}.",
                    nameof(operations));
            }
        }
    }
}
