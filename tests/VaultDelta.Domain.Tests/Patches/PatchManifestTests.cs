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
    public void FromDiff_orders_directory_tree_operations_for_safe_application()
    {
        SnapshotInventory baseline = Inventory(
            Directory("Old"),
            Directory("Old/Nested"),
            File("Old/Nested/renamed.md", 'a', 1),
            Directory("Removed"),
            Directory("Removed/Child"),
            File("Removed/Child/deleted.md", 'b', 1));
        SnapshotInventory target = Inventory(
            Directory("ZMoved"),
            Directory("ZMoved/Nested"),
            File("ZMoved/Nested/renamed.md", 'a', 1));

        PatchManifest manifest = PatchManifest.FromDiff(
            "patch-directory-move",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            baseline,
            target,
            DiffEngine.Compare(baseline, target));

        int IndexOf(PatchOperationType type, string path) => manifest.Operations
            .Select((operation, index) => (operation, index))
            .Single(item => item.operation.Type == type
                && StringComparer.Ordinal.Equals(
                    (item.operation.TargetPath ?? item.operation.BasePath)!.Value,
                    path))
            .index;

        Assert.True(IndexOf(PatchOperationType.Add, "ZMoved") < IndexOf(PatchOperationType.Rename, "ZMoved/Nested/renamed.md"));
        Assert.True(IndexOf(PatchOperationType.Rename, "ZMoved/Nested/renamed.md") < IndexOf(PatchOperationType.Delete, "Old/Nested"));
        Assert.True(IndexOf(PatchOperationType.Delete, "Old/Nested") < IndexOf(PatchOperationType.Delete, "Old"));
        Assert.True(IndexOf(PatchOperationType.Delete, "Removed/Child/deleted.md") < IndexOf(PatchOperationType.Delete, "Removed/Child"));
        Assert.True(IndexOf(PatchOperationType.Delete, "Removed/Child") < IndexOf(PatchOperationType.Delete, "Removed"));
        Assert.Equal(
            Enumerable.Range(1, manifest.Operations.Count).Select(index => index * 10),
            manifest.Operations.Select(operation => operation.Sequence));
    }

    [Fact]
    public void FromDiff_orders_file_and_directory_type_replacements()
    {
        SnapshotInventory baseline = Inventory(
            File("FileToDirectory", 'a', 1),
            Directory("DirectoryToFile"),
            File("DirectoryToFile/child.md", 'b', 2));
        SnapshotInventory target = Inventory(
            Directory("FileToDirectory"),
            File("FileToDirectory/child.md", 'c', 3),
            File("DirectoryToFile", 'd', 4));

        PatchManifest manifest = PatchManifest.FromDiff(
            "patch-type-replacements",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            baseline,
            target,
            DiffEngine.Compare(baseline, target));

        int IndexOf(PatchOperationType type, SnapshotEntryKind kind, string path) => manifest.Operations
            .Select((operation, index) => (operation, index))
            .Single(item => item.operation.Type == type
                && item.operation.EntryKind == kind
                && StringComparer.Ordinal.Equals(
                    (item.operation.TargetPath ?? item.operation.BasePath)!.Value,
                    path))
            .index;

        Assert.True(
            IndexOf(PatchOperationType.Delete, SnapshotEntryKind.File, "FileToDirectory")
            < IndexOf(PatchOperationType.Add, SnapshotEntryKind.Directory, "FileToDirectory"));
        Assert.True(
            IndexOf(PatchOperationType.Delete, SnapshotEntryKind.File, "DirectoryToFile/child.md")
            < IndexOf(PatchOperationType.Delete, SnapshotEntryKind.Directory, "DirectoryToFile"));
        Assert.True(
            IndexOf(PatchOperationType.Delete, SnapshotEntryKind.Directory, "DirectoryToFile")
            < IndexOf(PatchOperationType.Add, SnapshotEntryKind.File, "DirectoryToFile"));
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
