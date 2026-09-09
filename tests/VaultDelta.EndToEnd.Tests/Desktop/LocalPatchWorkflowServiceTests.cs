using VaultDelta.Application.Apply;
using VaultDelta.Application.Compare;
using VaultDelta.Application.Patches;
using VaultDelta.Desktop.Services;
using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;
using VaultDelta.Infrastructure.Apply;
using VaultDelta.Infrastructure.FileSystem;
using VaultDelta.Infrastructure.Hashing;
using VaultDelta.Infrastructure.Patches;
using VaultDelta.Infrastructure.Platform;

namespace VaultDelta.EndToEnd.Tests.Desktop;

public sealed class LocalPatchWorkflowServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vaultdelta-desktop-patch-{Guid.NewGuid():N}");

    [Fact]
    public async Task BuildAsync_publishes_a_verified_zip_from_the_compared_target()
    {
        string baselineRoot = Path.Combine(_root, "baseline");
        string targetRoot = Path.Combine(_root, "target");
        string output = Path.Combine(_root, "transfer", "delta.zip");
        Directory.CreateDirectory(baselineRoot);
        Directory.CreateDirectory(targetRoot);
        await File.WriteAllTextAsync(Path.Combine(baselineRoot, "note.md"), "before");
        await File.WriteAllTextAsync(Path.Combine(targetRoot, "note.md"), "after");
        await File.WriteAllTextAsync(Path.Combine(targetRoot, "added.md"), "new");
        Sha256ContentHasher hasher = new();
        SnapshotInventory baseline = SnapshotInventory.Create(
            "rules",
            [await EntryAsync(baselineRoot, "note.md", hasher)]);
        SnapshotInventory target = SnapshotInventory.Create(
            "rules",
            [await EntryAsync(targetRoot, "note.md", hasher), await EntryAsync(targetRoot, "added.md", hasher)]);
        DiffSet differences = DiffEngine.Compare(baseline, target);
        CompareResult comparison = new(
            baseline,
            target,
            differences,
            new CompareSummary(1, 1, 0, 0, 0, 2, 8, 0));
        LocalPatchWorkflowService service = CreateService(hasher);

        await service.BuildAsync(comparison, targetRoot, output);
        PackageInspectionResult inspection = await service.InspectAsync(output);

        Assert.True(File.Exists(output));
        Assert.Equal(2, inspection.Manifest.Operations.Count);
        Assert.Equal(2, inspection.VerifiedPayloadCount);
        Assert.Equal(8, inspection.VerifiedPayloadBytes);
        Assert.Equal(baseline.SnapshotId, inspection.Manifest.BaseSnapshotId);
        Assert.Equal(target.SnapshotId, inspection.Manifest.TargetSnapshotId);
        Assert.Equal("0.1.1", inspection.Manifest.GeneratorVersion);
    }

    private static LocalPatchWorkflowService CreateService(Sha256ContentHasher hasher)
    {
        PackageInspector inspector = new(new ZipPackageReader());
        PackageBuilder builder = new(new ZipPackageWriter(new DirectoryPackageWriter(hasher), inspector));
        LocalTargetStateReader stateReader = new(hasher);
        BaselineValidator baselineValidator = new(stateReader);
        LocalApplyFileOperations fileOperations = new();
        PlatformCapabilityProbe capabilities = new();
        ApplyWorkflow apply = new(
            inspector,
            baselineValidator,
            new PatchPayloadStager(hasher),
            new JsonApplyJournalStore(),
            new TargetLockManager(),
            new BackupStore(hasher),
            new AtomicFileWriter(hasher),
            fileOperations,
            stateReader,
            capabilities);
        RollbackWorkflow rollback = new(
            new JsonApplyJournalStore(),
            new TargetLockManager(),
            fileOperations,
            stateReader,
            capabilities);
        return new LocalPatchWorkflowService(builder, inspector, baselineValidator, apply, rollback);
    }

    private static async Task<SnapshotEntry> EntryAsync(string root, string relativePath, Sha256ContentHasher hasher)
    {
        string path = Path.Combine(root, relativePath);
        FileInfo file = new(path);
        string hash = await hasher.ComputeSha256Async(path);
        return new SnapshotEntry(
            RelativePath.Parse(relativePath),
            SnapshotEntryKind.File,
            new FileFingerprint(file.Length, file.LastWriteTimeUtc, hash));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
