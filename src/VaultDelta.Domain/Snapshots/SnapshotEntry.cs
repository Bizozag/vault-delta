using VaultDelta.Domain.Paths;

namespace VaultDelta.Domain.Snapshots;

public sealed record SnapshotEntry
{
    public SnapshotEntry(
        RelativePath path,
        SnapshotEntryKind kind,
        FileFingerprint? fingerprint)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown snapshot entry kind.");
        }

        if (kind == SnapshotEntryKind.File && fingerprint is null)
        {
            throw new ArgumentException("File entries require a fingerprint.", nameof(fingerprint));
        }

        if (kind == SnapshotEntryKind.Directory && fingerprint is not null)
        {
            throw new ArgumentException("Directory entries cannot have a file fingerprint.", nameof(fingerprint));
        }

        Path = path;
        Kind = kind;
        Fingerprint = fingerprint;
    }

    public RelativePath Path { get; }

    public SnapshotEntryKind Kind { get; }

    public FileFingerprint? Fingerprint { get; }
}
