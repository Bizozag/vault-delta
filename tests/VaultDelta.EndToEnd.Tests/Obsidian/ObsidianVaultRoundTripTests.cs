using System.IO.Compression;
using System.Security.Cryptography;
using VaultDelta.Application.Apply;
using VaultDelta.Application.Compare;
using VaultDelta.Application.Patches;
using VaultDelta.Application.Snapshots;
using VaultDelta.Domain.Apply;
using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Rules;
using VaultDelta.Infrastructure.Apply;
using VaultDelta.Infrastructure.FileSystem;
using VaultDelta.Infrastructure.Hashing;
using VaultDelta.Infrastructure.Patches;
using VaultDelta.Infrastructure.Platform;

namespace VaultDelta.EndToEnd.Tests.Obsidian;

public sealed class ObsidianVaultRoundTripTests
{
    private const string ExpectedBaselineSnapshotId = "sha256:30652d0fe9ede9a6ee1615b6d4cb5baeb69bb25444bf42e94335304d5382ceb9";
    private const string ExpectedTargetSnapshotId = "sha256:d0885a865cc3cf93e00ed29c842fdfee9f6b3cadec4ae81d7ba738bb12e5bfa3";
    private const string ExpectedManifestSha256 = "013928df64fd7decdb2cb3ea4ec2c94955a7358a3225f26ad837f090c08b9b1f";

    [Fact]
    public async Task Fixed_obsidian_fixture_round_trips_compare_zip_apply_and_rollback()
    {
        using ObsidianVaultFixture fixture = new();
        Sha256ContentHasher hasher = new();
        CompareWorkflow compare = new(new SnapshotScanner(new LocalFileSystem(), hasher));

        CompareResult comparison = await compare.RunAsync(
            new CompareRequest(fixture.BaselineRoot, fixture.TargetRoot, ObsidianDefaultRules.Create()));

        AssertCoverage(comparison);
        PatchManifest manifest = PatchManifest.FromDiff(
            "obsidian-e2e-fixture",
            new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero),
            "0.1.0",
            comparison.Baseline,
            comparison.Target,
            comparison.Differences);
        PackageInspector inspector = new(new ZipPackageReader());
        await new PackageBuilder(new ZipPackageWriter(new DirectoryPackageWriter(hasher), inspector))
            .BuildAsync(manifest, fixture.TargetRoot, fixture.PackagePath);
        PackageInspectionResult inspection = await inspector.InspectAsync(fixture.PackagePath);

        Assert.Equal(manifest.PatchId, inspection.Manifest.PatchId);
        Assert.Equal(manifest.Summary.PayloadBytes, inspection.VerifiedPayloadBytes);
        AssertArchiveCoverage(fixture.PackagePath);

        Dictionary<string, byte[]> baselineTree = ObsidianVaultFixture.ReadManagedTree(fixture.AppliedRoot);
        ApplyResult applied = await CreateApplyWorkflow(hasher, inspector).ApplyAsync(
            new ApplyRequest(fixture.PackagePath, fixture.AppliedRoot, fixture.TransactionRoot, "obsidian-fixture"));

        Assert.True(applied.Succeeded, applied.Error);
        AssertTreesEqual(
            ObsidianVaultFixture.ReadManagedTree(fixture.TargetRoot),
            ObsidianVaultFixture.ReadManagedTree(fixture.AppliedRoot));
        Assert.Contains("Target trash", await File.ReadAllTextAsync(Path.Combine(fixture.TargetRoot, ".trash", "Discarded.md")));
        Assert.Contains("Target trash", await File.ReadAllTextAsync(Path.Combine(fixture.AppliedRoot, ".trash", "Discarded.md")));

        RollbackResult rollback = await CreateRollbackWorkflow(hasher).RollbackAsync(applied.JournalPath!);

        Assert.True(rollback.Succeeded, rollback.Error);
        Assert.Equal(ApplyJournalStatus.RolledBack, rollback.Status);
        AssertTreesEqual(baselineTree, ObsidianVaultFixture.ReadManagedTree(fixture.AppliedRoot));
        Assert.Contains("Baseline trash", await File.ReadAllTextAsync(Path.Combine(fixture.AppliedRoot, ".trash", "Discarded.md")));
    }

    [Fact]
    public async Task Fixed_fixture_produces_deterministic_snapshot_diff_and_manifest_operations()
    {
        using ObsidianVaultFixture fixture = new();
        Sha256ContentHasher hasher = new();
        CompareWorkflow compare = new(new SnapshotScanner(new LocalFileSystem(), hasher));

        CompareResult first = await compare.RunAsync(
            new CompareRequest(fixture.BaselineRoot, fixture.TargetRoot, ObsidianDefaultRules.Create()));
        CompareResult second = await compare.RunAsync(
            new CompareRequest(fixture.BaselineRoot, fixture.TargetRoot, ObsidianDefaultRules.Create()));
        PatchManifest firstManifest = PatchManifest.FromDiff(
            "fixed",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            first.Baseline,
            first.Target,
            first.Differences);
        PatchManifest secondManifest = PatchManifest.FromDiff(
            "fixed",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            second.Baseline,
            second.Target,
            second.Differences);

        Assert.Equal(first.Baseline.SnapshotId, second.Baseline.SnapshotId);
        Assert.Equal(first.Target.SnapshotId, second.Target.SnapshotId);
        byte[] firstJson = PatchManifestJson.Serialize(firstManifest);
        Console.WriteLine($"Baseline snapshot: {first.Baseline.SnapshotId}");
        Console.WriteLine($"Target snapshot: {first.Target.SnapshotId}");
        Console.WriteLine($"Manifest SHA-256: {Convert.ToHexStringLower(SHA256.HashData(firstJson))}");
        Assert.Equal(ExpectedBaselineSnapshotId, first.Baseline.SnapshotId);
        Assert.Equal(ExpectedTargetSnapshotId, first.Target.SnapshotId);
        Assert.Equal(
            firstManifest.Operations.Select(OperationIdentity),
            secondManifest.Operations.Select(OperationIdentity));
        Assert.Equal(firstJson, PatchManifestJson.Serialize(secondManifest));
        Assert.Equal(
            ExpectedManifestSha256,
            Convert.ToHexStringLower(SHA256.HashData(firstJson)));
    }

    private static void AssertCoverage(CompareResult comparison)
    {
        Dictionary<string, DiffEntryType> byPath = comparison.Differences.Entries
            .Where(entry => entry.Type != DiffEntryType.Unchanged)
            .ToDictionary(entry => (entry.TargetPath ?? entry.BasePath)!.Value, entry => entry.Type, StringComparer.Ordinal);

        Assert.Equal(DiffEntryType.Modified, byPath["Notes/Welcome.md"]);
        Assert.Equal(DiffEntryType.Renamed, byPath["Notes/Renamed.md"]);
        Assert.Equal(DiffEntryType.Deleted, byPath["Notes/Removed.md"]);
        Assert.Equal(DiffEntryType.Added, byPath["Projects/发布检查.md"]);
        Assert.Equal(DiffEntryType.Modified, byPath["Canvas/Workflow.canvas"]);
        Assert.Equal(DiffEntryType.Modified, byPath["Excalidraw/System.excalidraw.md"]);
        Assert.Equal(DiffEntryType.Modified, byPath["Attachments/diagram.png"]);
        Assert.Equal(DiffEntryType.Modified, byPath[".obsidian/plugins/sample/main.js"]);
        Assert.Equal(DiffEntryType.Modified, byPath[".obsidian/plugins/sample/manifest.json"]);
        Assert.Equal(DiffEntryType.Added, byPath[".obsidian/themes/Local Theme/theme.css"]);
        Assert.Equal(DiffEntryType.Modified, byPath[".trash/Discarded.md"]);
        Assert.Equal(DiffEntryType.Modified, byPath[".obsidian/workspace.json"]);
    }

    private static void AssertArchiveCoverage(string packagePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        string[] entries = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToArray();
        Assert.Contains(entries, path => path == "files/.trash/Discarded.md");
        Assert.Contains(entries, path => path == "files/.obsidian/workspace.json");
        Assert.Contains(entries, path => path == "files/.obsidian/plugins/sample/main.js");
        Assert.Contains(entries, path => path == "files/.obsidian/themes/Local Theme/theme.css");
        Assert.Contains(entries, path => path == "files/Attachments/diagram.png");
    }

    private static ApplyWorkflow CreateApplyWorkflow(Sha256ContentHasher hasher, PackageInspector inspector)
    {
        LocalTargetStateReader states = new(hasher);
        return new ApplyWorkflow(
            inspector,
            new BaselineValidator(states),
            new PatchPayloadStager(hasher),
            new JsonApplyJournalStore(),
            new TargetLockManager(),
            new BackupStore(hasher),
            new AtomicFileWriter(hasher),
            new LocalApplyFileOperations(),
            states,
            new PlatformCapabilityProbe());
    }

    private static RollbackWorkflow CreateRollbackWorkflow(Sha256ContentHasher hasher) =>
        new(
            new JsonApplyJournalStore(),
            new TargetLockManager(),
            new LocalApplyFileOperations(),
            new LocalTargetStateReader(hasher),
            new PlatformCapabilityProbe());

    private static string OperationIdentity(PatchOperation operation) =>
        $"{operation.Sequence}:{operation.Type}:{operation.EntryKind}:{operation.BasePath}:{operation.TargetPath}:{operation.PayloadPath}:{operation.OldFingerprint?.Sha256}:{operation.NewFingerprint?.Sha256}";

    private static void AssertTreesEqual(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach ((string path, byte[] expectedBytes) in expected)
        {
            Assert.Equal(expectedBytes, actual[path]);
        }
    }
}
