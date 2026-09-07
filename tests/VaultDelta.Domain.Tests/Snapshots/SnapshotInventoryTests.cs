using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Domain.Tests.Snapshots;

public sealed class SnapshotInventoryTests
{
    [Fact]
    public void Create_sorts_entries_by_canonical_path()
    {
        SnapshotInventory inventory = SnapshotInventory.Create(
            "obsidian-default-v1",
            [
                File("z-last.md", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"),
                File("A-first.md", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            ]);

        Assert.Equal(["A-first.md", "z-last.md"], inventory.Entries.Select(entry => entry.Path.Value));
    }

    [Fact]
    public void Create_rejects_exact_duplicate_paths()
    {
        SnapshotEntry first = File("Notes/readme.md", Hash('a'));
        SnapshotEntry duplicate = File("Notes/readme.md", Hash('b'));

        Assert.Throws<ArgumentException>(() =>
            SnapshotInventory.Create("obsidian-default-v1", [first, duplicate]));
    }

    [Fact]
    public void Create_rejects_portable_case_collisions()
    {
        SnapshotEntry upper = File("Notes/Readme.md", Hash('a'));
        SnapshotEntry lower = File("notes/readme.md", Hash('b'));

        Assert.Throws<ArgumentException>(() =>
            SnapshotInventory.Create("obsidian-default-v1", [upper, lower]));
    }

    [Fact]
    public void Snapshot_id_is_independent_of_input_order_and_timestamps()
    {
        SnapshotEntry firstVersion = File("A.md", Hash('a'), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        SnapshotEntry secondVersion = File("B.md", Hash('b'), new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        SnapshotInventory first = SnapshotInventory.Create("rules-v1", [firstVersion, secondVersion]);

        SnapshotEntry firstWithNewTimestamp = File("A.md", Hash('a'), new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        SnapshotEntry secondWithNewTimestamp = File("B.md", Hash('b'), new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        SnapshotInventory second = SnapshotInventory.Create("rules-v1", [secondWithNewTimestamp, firstWithNewTimestamp]);

        Assert.Equal(first.SnapshotId, second.SnapshotId);
    }

    [Fact]
    public void Snapshot_id_changes_when_rules_path_or_content_changes()
    {
        SnapshotInventory baseline = SnapshotInventory.Create("rules-v1", [File("A.md", Hash('a'))]);
        SnapshotInventory changedRules = SnapshotInventory.Create("rules-v2", [File("A.md", Hash('a'))]);
        SnapshotInventory changedPath = SnapshotInventory.Create("rules-v1", [File("B.md", Hash('a'))]);
        SnapshotInventory changedContent = SnapshotInventory.Create("rules-v1", [File("A.md", Hash('b'))]);

        Assert.NotEqual(baseline.SnapshotId, changedRules.SnapshotId);
        Assert.NotEqual(baseline.SnapshotId, changedPath.SnapshotId);
        Assert.NotEqual(baseline.SnapshotId, changedContent.SnapshotId);
    }

    [Fact]
    public void File_fingerprint_normalizes_hash_to_lowercase()
    {
        FileFingerprint fingerprint = new(12, DateTimeOffset.UnixEpoch, Hash('A'));

        Assert.Equal(Hash('a'), fingerprint.Sha256);
    }

    [Theory]
    [InlineData(-1, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(0, "too-short")]
    [InlineData(0, "gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void File_fingerprint_rejects_invalid_values(long length, string hash)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FileFingerprint(length, DateTimeOffset.UnixEpoch, hash));
    }

    [Fact]
    public void File_entry_requires_a_fingerprint()
    {
        Assert.Throws<ArgumentException>(() =>
            new SnapshotEntry(RelativePath.Parse("note.md"), SnapshotEntryKind.File, null));
    }

    [Fact]
    public void Directory_entry_rejects_a_file_fingerprint()
    {
        FileFingerprint fingerprint = new(0, DateTimeOffset.UnixEpoch, Hash('a'));

        Assert.Throws<ArgumentException>(() =>
            new SnapshotEntry(RelativePath.Parse("folder"), SnapshotEntryKind.Directory, fingerprint));
    }

    private static SnapshotEntry File(
        string path,
        string sha256,
        DateTimeOffset? lastWriteTimeUtc = null) =>
        new(
            RelativePath.Parse(path),
            SnapshotEntryKind.File,
            new FileFingerprint(10, lastWriteTimeUtc ?? DateTimeOffset.UnixEpoch, sha256));

    private static string Hash(char value) => new(value, 64);
}
