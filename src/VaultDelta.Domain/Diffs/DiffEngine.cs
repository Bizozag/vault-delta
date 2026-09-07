using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Domain.Diffs;

public static class DiffEngine
{
    public static DiffSet Compare(SnapshotInventory baseline, SnapshotInventory target)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(target);

        if (!StringComparer.Ordinal.Equals(baseline.RulesId, target.RulesId))
        {
            throw new ArgumentException("Snapshots created with different rules cannot be compared.", nameof(target));
        }

        Dictionary<string, SnapshotEntry> baseByPath = baseline.Entries.ToDictionary(
            entry => entry.Path.Value,
            StringComparer.Ordinal);
        Dictionary<string, SnapshotEntry> targetByPath = target.Entries.ToDictionary(
            entry => entry.Path.Value,
            StringComparer.Ordinal);
        List<DiffEntry> results = [];
        List<SnapshotEntry> unmatchedBase = [];
        List<SnapshotEntry> unmatchedTarget = [];

        foreach (SnapshotEntry baseEntry in baseline.Entries)
        {
            if (!targetByPath.TryGetValue(baseEntry.Path.Value, out SnapshotEntry? targetEntry))
            {
                unmatchedBase.Add(baseEntry);
                continue;
            }

            if (baseEntry.Kind != targetEntry.Kind)
            {
                results.Add(new DiffEntry(DiffEntryType.Deleted, baseEntry, null));
                results.Add(new DiffEntry(DiffEntryType.Added, null, targetEntry));
                continue;
            }

            DiffEntryType type = HasSameContent(baseEntry, targetEntry)
                ? DiffEntryType.Unchanged
                : DiffEntryType.Modified;
            results.Add(new DiffEntry(type, baseEntry, targetEntry));
        }

        foreach (SnapshotEntry targetEntry in target.Entries)
        {
            if (!baseByPath.ContainsKey(targetEntry.Path.Value))
            {
                unmatchedTarget.Add(targetEntry);
            }
        }

        MatchUniqueRenames(unmatchedBase, unmatchedTarget, results);
        results.Sort(CompareEntries);
        return new DiffSet(results);
    }

    private static void MatchUniqueRenames(
        IReadOnlyCollection<SnapshotEntry> unmatchedBase,
        IReadOnlyCollection<SnapshotEntry> unmatchedTarget,
        List<DiffEntry> results)
    {
        Dictionary<ContentIdentity, List<SnapshotEntry>> baseFiles = GroupFilesByContent(unmatchedBase);
        Dictionary<ContentIdentity, List<SnapshotEntry>> targetFiles = GroupFilesByContent(unmatchedTarget);
        HashSet<SnapshotEntry> renamedBase = [];
        HashSet<SnapshotEntry> renamedTarget = [];

        foreach ((ContentIdentity identity, List<SnapshotEntry> baseCandidates) in baseFiles)
        {
            if (baseCandidates.Count != 1
                || !targetFiles.TryGetValue(identity, out List<SnapshotEntry>? targetCandidates)
                || targetCandidates.Count != 1)
            {
                continue;
            }

            SnapshotEntry baseEntry = baseCandidates[0];
            SnapshotEntry targetEntry = targetCandidates[0];
            renamedBase.Add(baseEntry);
            renamedTarget.Add(targetEntry);
            results.Add(new DiffEntry(DiffEntryType.Renamed, baseEntry, targetEntry));
        }

        foreach (SnapshotEntry entry in unmatchedBase.Where(entry => !renamedBase.Contains(entry)))
        {
            results.Add(new DiffEntry(DiffEntryType.Deleted, entry, null));
        }

        foreach (SnapshotEntry entry in unmatchedTarget.Where(entry => !renamedTarget.Contains(entry)))
        {
            results.Add(new DiffEntry(DiffEntryType.Added, null, entry));
        }
    }

    private static Dictionary<ContentIdentity, List<SnapshotEntry>> GroupFilesByContent(
        IEnumerable<SnapshotEntry> entries) =>
        entries
            .Where(entry => entry.Kind == SnapshotEntryKind.File)
            .GroupBy(entry => new ContentIdentity(entry.Fingerprint!.Length, entry.Fingerprint.Sha256))
            .ToDictionary(group => group.Key, group => group.ToList());

    private static bool HasSameContent(SnapshotEntry baseline, SnapshotEntry target)
    {
        if (baseline.Kind == SnapshotEntryKind.Directory)
        {
            return true;
        }

        return baseline.Fingerprint!.Length == target.Fingerprint!.Length
            && StringComparer.Ordinal.Equals(baseline.Fingerprint.Sha256, target.Fingerprint.Sha256);
    }

    private static int CompareEntries(DiffEntry left, DiffEntry right)
    {
        string leftPath = left.TargetPath?.Value ?? left.BasePath!.Value;
        string rightPath = right.TargetPath?.Value ?? right.BasePath!.Value;
        int pathComparison = StringComparer.Ordinal.Compare(leftPath, rightPath);
        return pathComparison != 0
            ? pathComparison
            : GetOperationPriority(left.Type).CompareTo(GetOperationPriority(right.Type));
    }

    private static int GetOperationPriority(DiffEntryType type) => type switch
    {
        DiffEntryType.Deleted => 1,
        DiffEntryType.Renamed => 2,
        DiffEntryType.Modified => 3,
        DiffEntryType.Added => 4,
        DiffEntryType.Unchanged => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown difference type."),
    };

    private readonly record struct ContentIdentity(long Length, string Sha256);
}
