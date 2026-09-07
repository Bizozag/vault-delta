using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Domain.Tests.Patches;

public sealed class PatchManifestTests
{
    [Fact]
    public void FromDiff_creates_stable_operations_and_summary()
    {
        SnapshotInventory baseline = Inventory(
            File("delete.md", 'a', 2),
            File("modify.md", 'b', 3),
            File("old-name.md", 'c', 4));
        SnapshotInventory target = Inventory(
            File("add.md", 'd', 5),
            File("modify.md", 'e', 6),
            File("new-name.md", 'c', 4));
        DiffSet differences = DiffEngine.Compare(baseline, target);

        PatchManifest manifest = PatchManifest.FromDiff(
            "patch-001",
            new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.Zero),
            "0.1.0",
            baseline,
            target,
            differences);

        Assert.Equal("1.0", manifest.SchemaVersion);
        Assert.Equal([PatchOperationType.Add, PatchOperationType.Delete, PatchOperationType.Modify, PatchOperationType.Rename], manifest.Operations.Select(x => x.Type));
        Assert.Equal(1, manifest.Summary.Added);
        Assert.Equal(1, manifest.Summary.Modified);
        Assert.Equal(1, manifest.Summary.Deleted);
        Assert.Equal(1, manifest.Summary.Renamed);
        Assert.Equal(11, manifest.Summary.PayloadBytes);
        Assert.Equal("files/add.md", manifest.Operations[0].PayloadPath!.Value);
    }

    [Fact]
    public void FromDiff_omits_unchanged_entries()
    {
        SnapshotInventory baseline = Inventory(File("same.md", 'a', 3));
        SnapshotInventory target = Inventory(File("same.md", 'a', 3));

        PatchManifest manifest = PatchManifest.FromDiff(
            "patch-001",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            baseline,
            target,
            DiffEngine.Compare(baseline, target));

        Assert.Empty(manifest.Operations);
        Assert.Equal(0, manifest.Summary.PayloadBytes);
    }

    [Fact]
    public void FromDiff_supports_directory_operations_without_payloads()
    {
        SnapshotInventory baseline = Inventory(Directory("OldEmpty"));
        SnapshotInventory target = Inventory(Directory("NewEmpty"));

        PatchManifest manifest = PatchManifest.FromDiff(
            "patch-001",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            baseline,
            target,
            DiffEngine.Compare(baseline, target));

        Assert.Collection(
            manifest.Operations,
            operation =>
            {
                Assert.Equal(PatchOperationType.Add, operation.Type);
                Assert.Equal(SnapshotEntryKind.Directory, operation.EntryKind);
                Assert.Null(operation.PayloadPath);
            },
            operation =>
            {
                Assert.Equal(PatchOperationType.Delete, operation.Type);
                Assert.Equal(SnapshotEntryKind.Directory, operation.EntryKind);
                Assert.Null(operation.PayloadPath);
            });
    }

    [Fact]
    public void Constructor_rejects_duplicate_sequences_and_target_paths()
    {
        FileFingerprint fingerprint = Fingerprint('a', 1);
        PatchOperation first = PatchOperation.Add(10, RelativePath.Parse("A.md"), fingerprint);
        PatchOperation duplicateSequence = PatchOperation.Add(10, RelativePath.Parse("B.md"), fingerprint);
        PatchOperation duplicateTarget = PatchOperation.Modify(20, RelativePath.Parse("A.md"), fingerprint, fingerprint);

        Assert.Throws<ArgumentException>(() => Manifest([first, duplicateSequence]));
        Assert.Throws<ArgumentException>(() => Manifest([first, duplicateTarget]));
    }

    private static PatchManifest Manifest(IReadOnlyList<PatchOperation> operations) =>
        new(
            "1.0",
            "patch-001",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            "rules-v1",
            "sha256:base",
            "sha256:target",
            new PatchSummary(0, 0, 0, 0, 0),
            operations);

    private static SnapshotInventory Inventory(params SnapshotEntry[] entries) =>
        SnapshotInventory.Create("rules-v1", entries);

    private static SnapshotEntry File(string path, char hashCharacter, long length) =>
        new(RelativePath.Parse(path), SnapshotEntryKind.File, Fingerprint(hashCharacter, length));

    private static SnapshotEntry Directory(string path) =>
        new(RelativePath.Parse(path), SnapshotEntryKind.Directory, null);

    private static FileFingerprint Fingerprint(char hashCharacter, long length) =>
        new(length, DateTimeOffset.UnixEpoch, new string(hashCharacter, 64));
}
