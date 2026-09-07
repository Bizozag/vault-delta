using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Domain.Patches;

public sealed record PatchOperation
{
    private PatchOperation(
        int sequence,
        PatchOperationType type,
        SnapshotEntryKind entryKind,
        RelativePath? basePath,
        RelativePath? targetPath,
        RelativePath? payloadPath,
        FileFingerprint? oldFingerprint,
        FileFingerprint? newFingerprint)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Operation sequence must be positive.");
        }

        Sequence = sequence;
        Type = type;
        EntryKind = entryKind;
        BasePath = basePath;
        TargetPath = targetPath;
        PayloadPath = payloadPath;
        OldFingerprint = oldFingerprint;
        NewFingerprint = newFingerprint;
    }

    public int Sequence { get; }

    public PatchOperationType Type { get; }

    public SnapshotEntryKind EntryKind { get; }

    public RelativePath? BasePath { get; }

    public RelativePath? TargetPath { get; }

    public RelativePath? PayloadPath { get; }

    public FileFingerprint? OldFingerprint { get; }

    public FileFingerprint? NewFingerprint { get; }

    public static PatchOperation Add(int sequence, RelativePath targetPath, FileFingerprint newFingerprint) =>
        new(
            sequence,
            PatchOperationType.Add,
            SnapshotEntryKind.File,
            null,
            targetPath,
            PayloadFor(targetPath),
            null,
            newFingerprint);

    public static PatchOperation Modify(
        int sequence,
        RelativePath path,
        FileFingerprint oldFingerprint,
        FileFingerprint newFingerprint) =>
        new(
            sequence,
            PatchOperationType.Modify,
            SnapshotEntryKind.File,
            path,
            path,
            PayloadFor(path),
            oldFingerprint,
            newFingerprint);

    public static PatchOperation Delete(int sequence, RelativePath basePath, FileFingerprint oldFingerprint) =>
        new(sequence, PatchOperationType.Delete, SnapshotEntryKind.File, basePath, null, null, oldFingerprint, null);

    public static PatchOperation Rename(
        int sequence,
        RelativePath basePath,
        RelativePath targetPath,
        FileFingerprint fingerprint) =>
        new(sequence, PatchOperationType.Rename, SnapshotEntryKind.File, basePath, targetPath, null, fingerprint, fingerprint);

    public static PatchOperation AddDirectory(int sequence, RelativePath targetPath) =>
        new(sequence, PatchOperationType.Add, SnapshotEntryKind.Directory, null, targetPath, null, null, null);

    public static PatchOperation DeleteDirectory(int sequence, RelativePath basePath) =>
        new(sequence, PatchOperationType.Delete, SnapshotEntryKind.Directory, basePath, null, null, null, null);

    public static PatchOperation RenameDirectory(
        int sequence,
        RelativePath basePath,
        RelativePath targetPath) =>
        new(sequence, PatchOperationType.Rename, SnapshotEntryKind.Directory, basePath, targetPath, null, null, null);

    public static PatchOperation Restore(
        int sequence,
        PatchOperationType type,
        SnapshotEntryKind entryKind,
        RelativePath? basePath,
        RelativePath? targetPath,
        RelativePath? payloadPath,
        FileFingerprint? oldFingerprint,
        FileFingerprint? newFingerprint)
    {
        PatchOperation restored = (type, entryKind) switch
        {
            (PatchOperationType.Add, SnapshotEntryKind.File) => Add(sequence, Require(targetPath), Require(newFingerprint)),
            (PatchOperationType.Modify, SnapshotEntryKind.File) => Modify(sequence, Require(targetPath), Require(oldFingerprint), Require(newFingerprint)),
            (PatchOperationType.Delete, SnapshotEntryKind.File) => Delete(sequence, Require(basePath), Require(oldFingerprint)),
            (PatchOperationType.Rename, SnapshotEntryKind.File) => Rename(sequence, Require(basePath), Require(targetPath), Require(newFingerprint)),
            (PatchOperationType.Add, SnapshotEntryKind.Directory) => AddDirectory(sequence, Require(targetPath)),
            (PatchOperationType.Delete, SnapshotEntryKind.Directory) => DeleteDirectory(sequence, Require(basePath)),
            (PatchOperationType.Rename, SnapshotEntryKind.Directory) => RenameDirectory(sequence, Require(basePath), Require(targetPath)),
            _ => throw new ArgumentException($"Unsupported operation combination: {type}/{entryKind}."),
        };

        if (restored.PayloadPath != payloadPath
            || restored.OldFingerprint != oldFingerprint
            || restored.NewFingerprint != newFingerprint)
        {
            throw new ArgumentException("Restored operation fields are inconsistent with its type and entry kind.");
        }

        return restored;
    }

    private static RelativePath PayloadFor(RelativePath targetPath) =>
        RelativePath.Parse($"files/{targetPath.Value}");

    private static T Require<T>(T? value) where T : class =>
        value ?? throw new ArgumentException("The operation is missing a required value.");
}
