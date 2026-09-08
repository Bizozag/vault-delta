using VaultDelta.Domain.Diffs;

namespace VaultDelta.Application.Compare;

public static class DifferenceRiskClassifier
{
    public static bool IsRisk(DiffEntry entry, DiffSet differences)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(differences);

        if (entry.Type is DiffEntryType.Deleted or DiffEntryType.Renamed)
        {
            return true;
        }

        string path = (entry.TargetPath ?? entry.BasePath)!.Value;
        if (path.Equals(".obsidian", StringComparison.Ordinal)
            || path.StartsWith(".obsidian/", StringComparison.Ordinal))
        {
            return true;
        }

        return differences.Entries.Any(other =>
            !ReferenceEquals(other, entry)
            && StringComparer.Ordinal.Equals(
                (other.TargetPath ?? other.BasePath)?.Value,
                path)
            && other.Type != entry.Type);
    }
}
