using System.Diagnostics;
using VaultDelta.Application.Compare;
using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Tests.Compare;

public sealed class DifferenceRiskClassifierTests
{
    [Fact]
    public void Large_added_file_set_is_classified_within_a_linear_work_budget()
    {
        SnapshotInventory baseline = SnapshotInventory.Create("rules", []);
        SnapshotInventory target = SnapshotInventory.Create("rules", Enumerable.Range(0, 100_000).Select(index =>
            new SnapshotEntry(RelativePath.Parse($"node_modules/{index:D6}.js"), SnapshotEntryKind.File,
                new FileFingerprint(1, DateTimeOffset.UnixEpoch, new string('a', 64)))));
        DiffSet differences = DiffEngine.Compare(baseline, target);
        Stopwatch timer = Stopwatch.StartNew();

        int risks = differences.Entries.Count(entry => DifferenceRiskClassifier.IsRisk(entry, differences));

        Assert.Equal(0, risks);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"Risk classification took {timer.Elapsed} for 100,000 entries.");
    }

    [Fact]
    public void Type_change_and_configuration_paths_remain_risks()
    {
        SnapshotInventory baseline = SnapshotInventory.Create("rules", [new SnapshotEntry(RelativePath.Parse("changed"), SnapshotEntryKind.Directory, null)]);
        SnapshotInventory target = SnapshotInventory.Create("rules", [
            new SnapshotEntry(RelativePath.Parse("changed"), SnapshotEntryKind.File, new FileFingerprint(1, DateTimeOffset.UnixEpoch, new string('a', 64))),
            new SnapshotEntry(RelativePath.Parse(".obsidian"), SnapshotEntryKind.Directory, null)]);
        DiffSet differences = DiffEngine.Compare(baseline, target);

        Assert.All(differences.Entries, entry => Assert.True(DifferenceRiskClassifier.IsRisk(entry, differences)));
    }
}
