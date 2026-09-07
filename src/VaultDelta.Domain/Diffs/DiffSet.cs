using System.Collections.ObjectModel;
using VaultDelta.Domain.Paths;

namespace VaultDelta.Domain.Diffs;

public sealed class DiffSet
{
    internal DiffSet(IEnumerable<DiffEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        DiffEntry[] materialized = entries.ToArray();

        HashSet<RelativePath> targetPaths = new(RelativePath.PortableComparer);
        foreach (DiffEntry entry in materialized)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.TargetPath is not null && !targetPaths.Add(entry.TargetPath))
            {
                throw new ArgumentException(
                    $"The difference set contains multiple results for target path {entry.TargetPath}.",
                    nameof(entries));
            }
        }

        Entries = new ReadOnlyCollection<DiffEntry>(materialized);
    }

    public IReadOnlyList<DiffEntry> Entries { get; }
}
