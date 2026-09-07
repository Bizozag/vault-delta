using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Domain.Diffs;

public sealed record DiffEntry
{
    public DiffEntry(DiffEntryType type, SnapshotEntry? baseEntry, SnapshotEntry? targetEntry)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown difference type.");
        }

        ValidateSides(type, baseEntry, targetEntry);
        Type = type;
        BaseEntry = baseEntry;
        TargetEntry = targetEntry;
    }

    public DiffEntryType Type { get; }

    public SnapshotEntry? BaseEntry { get; }

    public SnapshotEntry? TargetEntry { get; }

    public RelativePath? BasePath => BaseEntry?.Path;

    public RelativePath? TargetPath => TargetEntry?.Path;

    private static void ValidateSides(
        DiffEntryType type,
        SnapshotEntry? baseEntry,
        SnapshotEntry? targetEntry)
    {
        bool valid = type switch
        {
            DiffEntryType.Added => baseEntry is null && targetEntry is not null,
            DiffEntryType.Deleted => baseEntry is not null && targetEntry is null,
            DiffEntryType.Modified or DiffEntryType.Renamed or DiffEntryType.Unchanged =>
                baseEntry is not null && targetEntry is not null,
            _ => false,
        };

        if (!valid)
        {
            throw new ArgumentException($"Difference type {type} has invalid base or target entries.");
        }

        if (type is DiffEntryType.Modified or DiffEntryType.Unchanged
            && !StringComparer.Ordinal.Equals(baseEntry!.Path.Value, targetEntry!.Path.Value))
        {
            throw new ArgumentException($"Difference type {type} requires identical paths.");
        }

        if (type == DiffEntryType.Renamed
            && StringComparer.Ordinal.Equals(baseEntry!.Path.Value, targetEntry!.Path.Value))
        {
            throw new ArgumentException("A rename requires different paths.");
        }
    }
}
