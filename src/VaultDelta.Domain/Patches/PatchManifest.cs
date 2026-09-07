using System.Collections.ObjectModel;
using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Domain.Patches;

public sealed class PatchManifest
{
    public const string CurrentSchemaVersion = "1.0";

    public PatchManifest(
        string schemaVersion,
        string patchId,
        DateTimeOffset createdAtUtc,
        string generatorVersion,
        string rulesId,
        string baseSnapshotId,
        string targetSnapshotId,
        PatchSummary summary,
        IReadOnlyList<PatchOperation> operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(patchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatorVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesId);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseSnapshotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSnapshotId);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(operations);

        ValidateOperations(operations);
        SchemaVersion = schemaVersion;
        PatchId = patchId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        GeneratorVersion = generatorVersion;
        RulesId = rulesId;
        BaseSnapshotId = baseSnapshotId;
        TargetSnapshotId = targetSnapshotId;
        Summary = summary;
        Operations = new ReadOnlyCollection<PatchOperation>(operations.ToArray());
    }

    public string SchemaVersion { get; }

    public string PatchId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public string GeneratorVersion { get; }

    public string RulesId { get; }

    public string BaseSnapshotId { get; }

    public string TargetSnapshotId { get; }

    public PatchSummary Summary { get; }

    public IReadOnlyList<PatchOperation> Operations { get; }

    public static PatchManifest FromDiff(
        string patchId,
        DateTimeOffset createdAtUtc,
        string generatorVersion,
        SnapshotInventory baseline,
        SnapshotInventory target,
        DiffSet differences)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(differences);

        List<PatchOperation> operations = [];
        int sequence = 10;

        foreach (DiffEntry difference in differences.Entries.Where(entry => entry.Type != DiffEntryType.Unchanged))
        {
            PatchOperation operation = difference.Type switch
            {
                DiffEntryType.Added when difference.TargetEntry?.Kind == SnapshotEntryKind.File =>
                    PatchOperation.Add(sequence, difference.TargetPath!, difference.TargetEntry.Fingerprint!),
                DiffEntryType.Added when difference.TargetEntry?.Kind == SnapshotEntryKind.Directory =>
                    PatchOperation.AddDirectory(sequence, difference.TargetPath!),
                DiffEntryType.Modified when difference.TargetEntry?.Kind == SnapshotEntryKind.File =>
                    PatchOperation.Modify(
                        sequence,
                        difference.TargetPath!,
                        difference.BaseEntry!.Fingerprint!,
                        difference.TargetEntry.Fingerprint!),
                DiffEntryType.Deleted when difference.BaseEntry?.Kind == SnapshotEntryKind.File =>
                    PatchOperation.Delete(sequence, difference.BasePath!, difference.BaseEntry.Fingerprint!),
                DiffEntryType.Deleted when difference.BaseEntry?.Kind == SnapshotEntryKind.Directory =>
                    PatchOperation.DeleteDirectory(sequence, difference.BasePath!),
                DiffEntryType.Renamed when difference.TargetEntry?.Kind == SnapshotEntryKind.File =>
                    PatchOperation.Rename(
                        sequence,
                        difference.BasePath!,
                        difference.TargetPath!,
                        difference.TargetEntry.Fingerprint!),
                DiffEntryType.Renamed when difference.TargetEntry?.Kind == SnapshotEntryKind.Directory =>
                    PatchOperation.RenameDirectory(sequence, difference.BasePath!, difference.TargetPath!),
                _ => throw new NotSupportedException(
                    $"Patch manifest v1 does not yet support {difference.Type} for {difference.BaseEntry?.Kind ?? difference.TargetEntry?.Kind}."),
            };

            operations.Add(operation);
            sequence += 10;
        }

        PatchSummary summary = new(
            operations.Count(operation => operation.Type == PatchOperationType.Add),
            operations.Count(operation => operation.Type == PatchOperationType.Modify),
            operations.Count(operation => operation.Type == PatchOperationType.Delete),
            operations.Count(operation => operation.Type == PatchOperationType.Rename),
            operations
                .Where(operation => operation.PayloadPath is not null)
                .Sum(operation => operation.NewFingerprint!.Length));

        return new PatchManifest(
            CurrentSchemaVersion,
            patchId,
            createdAtUtc,
            generatorVersion,
            baseline.RulesId,
            baseline.SnapshotId,
            target.SnapshotId,
            summary,
            operations);
    }

    private static void ValidateOperations(IReadOnlyList<PatchOperation> operations)
    {
        HashSet<int> sequences = [];
        HashSet<RelativePath> targetPaths = new(RelativePath.PortableComparer);
        int previousSequence = 0;

        foreach (PatchOperation operation in operations)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (!sequences.Add(operation.Sequence) || operation.Sequence <= previousSequence)
            {
                throw new ArgumentException("Operation sequences must be unique and strictly increasing.", nameof(operations));
            }

            if (operation.TargetPath is not null && !targetPaths.Add(operation.TargetPath))
            {
                throw new ArgumentException($"Multiple operations target {operation.TargetPath}.", nameof(operations));
            }

            previousSequence = operation.Sequence;
        }
    }
}
