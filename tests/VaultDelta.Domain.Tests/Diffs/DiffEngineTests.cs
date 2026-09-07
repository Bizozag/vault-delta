using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Domain.Tests.Diffs;

public sealed class DiffEngineTests
{
    [Fact]
    public void Compare_classifies_added_modified_deleted_and_unchanged_files()
    {
        SnapshotInventory baseline = Inventory(
            File("deleted.md", 'a'),
            File("modified.md", 'b'),
            File("unchanged.md", 'c'));
        SnapshotInventory target = Inventory(
            File("added.md", 'd'),
            File("modified.md", 'e'),
            File("unchanged.md", 'c'));

        DiffSet result = DiffEngine.Compare(baseline, target);

        Assert.Equal(DiffEntryType.Added, Assert.Single(result.Entries, entry => entry.TargetPath?.Value == "added.md").Type);
        Assert.Equal(DiffEntryType.Modified, Assert.Single(result.Entries, entry => entry.TargetPath?.Value == "modified.md").Type);
        Assert.Equal(DiffEntryType.Deleted, Assert.Single(result.Entries, entry => entry.BasePath?.Value == "deleted.md").Type);
        Assert.Equal(DiffEntryType.Unchanged, Assert.Single(result.Entries, entry => entry.TargetPath?.Value == "unchanged.md").Type);
    }

    [Fact]
    public void Compare_detects_a_unique_content_rename()
    {
        SnapshotInventory baseline = Inventory(File("Old/name.md", 'a'));
        SnapshotInventory target = Inventory(File("New/name.md", 'a'));

        DiffEntry rename = Assert.Single(DiffEngine.Compare(baseline, target).Entries);

        Assert.Equal(DiffEntryType.Renamed, rename.Type);
        Assert.Equal("Old/name.md", rename.BasePath!.Value);
        Assert.Equal("New/name.md", rename.TargetPath!.Value);
    }

    [Fact]
    public void Compare_does_not_guess_when_identical_content_has_multiple_candidates()
    {
        SnapshotInventory baseline = Inventory(File("Old/A.md", 'a'), File("Old/B.md", 'a'));
        SnapshotInventory target = Inventory(File("New/A.md", 'a'), File("New/B.md", 'a'));

        DiffSet result = DiffEngine.Compare(baseline, target);

        Assert.Equal(2, result.Entries.Count(entry => entry.Type == DiffEntryType.Added));
        Assert.Equal(2, result.Entries.Count(entry => entry.Type == DiffEntryType.Deleted));
        Assert.DoesNotContain(result.Entries, entry => entry.Type == DiffEntryType.Renamed);
    }

    [Fact]
    public void Compare_treats_a_case_only_path_change_as_a_rename()
    {
        SnapshotInventory baseline = Inventory(File("Notes/readme.md", 'a'));
        SnapshotInventory target = Inventory(File("Notes/README.md", 'a'));

        DiffEntry rename = Assert.Single(DiffEngine.Compare(baseline, target).Entries);

        Assert.Equal(DiffEntryType.Renamed, rename.Type);
    }

    [Fact]
    public void Compare_expands_a_file_to_directory_type_change_into_delete_and_add()
    {
        SnapshotInventory baseline = Inventory(File("Archive", 'a'));
        SnapshotInventory target = Inventory(Directory("Archive"));

        DiffSet result = DiffEngine.Compare(baseline, target);

        Assert.Collection(
            result.Entries,
            entry => Assert.Equal(DiffEntryType.Deleted, entry.Type),
            entry => Assert.Equal(DiffEntryType.Added, entry.Type));
    }

    [Fact]
    public void Compare_ignores_timestamp_only_changes()
    {
        SnapshotInventory baseline = Inventory(File("note.md", 'a', DateTimeOffset.UnixEpoch));
        SnapshotInventory target = Inventory(File("note.md", 'a', DateTimeOffset.UnixEpoch.AddDays(1)));

        DiffEntry entry = Assert.Single(DiffEngine.Compare(baseline, target).Entries);

        Assert.Equal(DiffEntryType.Unchanged, entry.Type);
    }

    [Fact]
    public void Compare_rejects_inventories_created_with_different_rules()
    {
        SnapshotInventory baseline = SnapshotInventory.Create("rules-a", [File("note.md", 'a')]);
        SnapshotInventory target = SnapshotInventory.Create("rules-b", [File("note.md", 'a')]);

        Assert.Throws<ArgumentException>(() => DiffEngine.Compare(baseline, target));
    }

    [Fact]
    public void Compare_is_deterministic_and_has_unique_target_paths()
    {
        SnapshotInventory baseline = Inventory(
            File("z.md", 'a'),
            File("rename-old.md", 'b'),
            File("modify.md", 'c'));
        SnapshotInventory target = Inventory(
            File("a.md", 'd'),
            File("rename-new.md", 'b'),
            File("modify.md", 'e'));

        DiffSet first = DiffEngine.Compare(baseline, target);
        DiffSet second = DiffEngine.Compare(baseline, target);

        Assert.Equal(first.Entries, second.Entries);
        RelativePath[] targetPaths = first.Entries
            .Where(entry => entry.TargetPath is not null)
            .Select(entry => entry.TargetPath!)
            .ToArray();
        Assert.Equal(targetPaths.Length, targetPaths.Distinct(RelativePath.PortableComparer).Count());
    }

    [Fact]
    public void Diff_entry_constructor_enforces_required_sides()
    {
        SnapshotEntry file = File("note.md", 'a');

        Assert.Throws<ArgumentException>(() => new DiffEntry(DiffEntryType.Added, file, null));
        Assert.Throws<ArgumentException>(() => new DiffEntry(DiffEntryType.Deleted, null, file));
        Assert.Throws<ArgumentException>(() => new DiffEntry(DiffEntryType.Renamed, file, null));
    }

    private static SnapshotInventory Inventory(params SnapshotEntry[] entries) =>
        SnapshotInventory.Create("rules-v1", entries);

    private static SnapshotEntry File(
        string path,
        char hashCharacter,
        DateTimeOffset? timestamp = null) =>
        new(
            RelativePath.Parse(path),
            SnapshotEntryKind.File,
            new FileFingerprint(10, timestamp ?? DateTimeOffset.UnixEpoch, new string(hashCharacter, 64)));

    private static SnapshotEntry Directory(string path) =>
        new(RelativePath.Parse(path), SnapshotEntryKind.Directory, null);
}
