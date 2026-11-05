using VaultDelta.Domain.Diffs;
using System.Runtime.CompilerServices;

namespace VaultDelta.Application.Compare;

public static class DifferenceRiskClassifier
{
    private static readonly ConditionalWeakTable<DiffSet, HashSet<string>> TypeChanges = new();
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

        return TypeChanges.GetValue(differences, BuildTypeChanges).Contains(path);
    }

    private static HashSet<string> BuildTypeChanges(DiffSet differences)
    {
        Dictionary<string, DiffEntryType> firstTypes = new(StringComparer.Ordinal);
        HashSet<string> changed = new(StringComparer.Ordinal);
        foreach (DiffEntry entry in differences.Entries)
        {
            string path = (entry.TargetPath ?? entry.BasePath)!.Value;
            if (!firstTypes.TryAdd(path, entry.Type) && firstTypes[path] != entry.Type)
            {
                changed.Add(path);
            }
        }

        return changed;
    }
}
