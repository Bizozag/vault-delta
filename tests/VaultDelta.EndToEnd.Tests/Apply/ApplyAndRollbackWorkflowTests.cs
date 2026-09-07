using VaultDelta.Application.Abstractions;
using VaultDelta.Application.Apply;
using VaultDelta.Application.Patches;
using VaultDelta.Domain.Apply;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;
using VaultDelta.Infrastructure.Apply;
using VaultDelta.Infrastructure.FileSystem;
using VaultDelta.Infrastructure.Hashing;
using VaultDelta.Infrastructure.Patches;
using VaultDelta.Infrastructure.Platform;

namespace VaultDelta.EndToEnd.Tests.Apply;

public sealed class ApplyAndRollbackWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vaultdelta-e2e-{Guid.NewGuid():N}");
    private readonly Sha256ContentHasher _hasher = new();

    public ApplyAndRollbackWorkflowTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Apply_then_rollback_round_trips_all_operation_types()
    {
        string vault = Path.Combine(_root, "vault");
        string source = Path.Combine(_root, "target-snapshot");
        string package = Path.Combine(_root, "patch");
        string transactions = Path.Combine(_root, "transactions");
        await CreateBaselineAsync(vault);
        await CreateTargetAsync(source);
        Dictionary<string, string> baseline = ReadTree(vault);
        PatchManifest manifest = await CreateManifestAsync(vault, source);
        await new DirectoryPackageWriter(_hasher).WriteAsync(manifest, source, package, CancellationToken.None);

        ApplyResult applied = await CreateApplyWorkflow().ApplyAsync(
            new ApplyRequest(package, vault, transactions, "round-trip"),
            CancellationToken.None);

        Assert.True(applied.Succeeded, applied.Error);
        Assert.Equal(ReadTree(source), ReadTree(vault));

        RollbackResult rolledBack = await CreateRollbackWorkflow().RollbackAsync(
            applied.JournalPath!,
            CancellationToken.None);

        Assert.True(rolledBack.Succeeded, rolledBack.Error);
        Assert.Equal(baseline, ReadTree(vault));
        ApplyJournal journal = await new JsonApplyJournalStore().LoadAsync(applied.JournalPath!, CancellationToken.None);
        Assert.Equal(ApplyJournalStatus.RolledBack, journal.Status);
        Assert.All(journal.Operations, operation => Assert.Equal(ApplyOperationStatus.RolledBack, operation.Status));

        RollbackResult repeated = await CreateRollbackWorkflow().RollbackAsync(
            applied.JournalPath!,
            CancellationToken.None);
        Assert.True(repeated.Succeeded);
        Assert.Equal(baseline, ReadTree(vault));
    }

    [Theory]
    [InlineData(ApplyFaultPoint.AfterBackup)]
    [InlineData(ApplyFaultPoint.AfterTargetMutation)]
    public async Task Interrupted_modify_is_recoverable(ApplyFaultPoint faultPoint)
    {
        string vault = Path.Combine(_root, $"vault-{faultPoint}");
        string source = Path.Combine(_root, $"source-{faultPoint}");
        string package = Path.Combine(_root, $"package-{faultPoint}");
        string transactions = Path.Combine(_root, $"transactions-{faultPoint}");
        Directory.CreateDirectory(vault);
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(vault, "note.md"), "before", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(source, "note.md"), "after", CancellationToken.None);
        FileFingerprint oldFingerprint = await FingerprintAsync(Path.Combine(vault, "note.md"));
        FileFingerprint newFingerprint = await FingerprintAsync(Path.Combine(source, "note.md"));
        PatchManifest manifest = Manifest(
            "fault-patch",
            PatchOperation.Modify(10, RelativePath.Parse("note.md"), oldFingerprint, newFingerprint));
        await new DirectoryPackageWriter(_hasher).WriteAsync(manifest, source, package, CancellationToken.None);

        ApplyResult result = await CreateApplyWorkflow(new ThrowingFaultInjector(faultPoint, 10)).ApplyAsync(
            new ApplyRequest(package, vault, transactions, $"fault-{faultPoint}"),
            CancellationToken.None);

        Assert.True(result.RequiresRollback);
        ApplyJournal failed = await new JsonApplyJournalStore().LoadAsync(result.JournalPath!, CancellationToken.None);
        Assert.Equal(ApplyJournalStatus.NeedsRollback, failed.Status);

        RollbackResult rollback = await CreateRollbackWorkflow().RollbackAsync(result.JournalPath!, CancellationToken.None);

        Assert.True(rollback.Succeeded, rollback.Error);
        Assert.Equal("before", await File.ReadAllTextAsync(Path.Combine(vault, "note.md"), CancellationToken.None));
    }

    [Fact]
    public async Task Interrupted_rollback_can_resume_without_repeating_a_mutation()
    {
        string vault = Path.Combine(_root, "retry-vault");
        string source = Path.Combine(_root, "retry-source");
        string package = Path.Combine(_root, "retry-package");
        string transactions = Path.Combine(_root, "retry-transactions");
        Directory.CreateDirectory(vault);
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "added.md"), "new", CancellationToken.None);
        FileFingerprint fingerprint = await FingerprintAsync(Path.Combine(source, "added.md"));
        PatchManifest manifest = Manifest(
            "retry-patch",
            PatchOperation.Add(10, RelativePath.Parse("added.md"), fingerprint));
        await new DirectoryPackageWriter(_hasher).WriteAsync(manifest, source, package, CancellationToken.None);
        ApplyResult applied = await CreateApplyWorkflow().ApplyAsync(
            new ApplyRequest(package, vault, transactions, "retry"),
            CancellationToken.None);
        Assert.True(applied.Succeeded, applied.Error);

        RollbackResult interrupted = await CreateRollbackWorkflow(
            new ThrowingFaultInjector(ApplyFaultPoint.AfterRollbackMutation, 10))
            .RollbackAsync(applied.JournalPath!, CancellationToken.None);

        Assert.False(interrupted.Succeeded);
        Assert.False(File.Exists(Path.Combine(vault, "added.md")));

        RollbackResult resumed = await CreateRollbackWorkflow().RollbackAsync(
            applied.JournalPath!,
            CancellationToken.None);

        Assert.True(resumed.Succeeded, resumed.Error);
        Assert.Empty(ReadTree(vault));
    }

    [Fact]
    public async Task Baseline_conflict_stops_before_lock_journal_or_target_write()
    {
        string vault = Path.Combine(_root, "conflict-vault");
        string source = Path.Combine(_root, "conflict-source");
        string package = Path.Combine(_root, "conflict-package");
        string transactions = Path.Combine(_root, "conflict-transactions");
        Directory.CreateDirectory(vault);
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(vault, "note.md"), "unexpected", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(source, "note.md"), "after", CancellationToken.None);
        PatchManifest manifest = Manifest(
            "conflict-patch",
            PatchOperation.Modify(
                10,
                RelativePath.Parse("note.md"),
                new FileFingerprint(6, DateTimeOffset.UnixEpoch, new string('a', 64)),
                await FingerprintAsync(Path.Combine(source, "note.md"))));
        await new DirectoryPackageWriter(_hasher).WriteAsync(manifest, source, package, CancellationToken.None);

        ApplyResult result = await CreateApplyWorkflow().ApplyAsync(
            new ApplyRequest(package, vault, transactions, "conflict"),
            CancellationToken.None);

        Assert.Null(result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal("unexpected", await File.ReadAllTextAsync(Path.Combine(vault, "note.md"), CancellationToken.None));
        Assert.False(Directory.Exists(transactions));
        Assert.False(File.Exists(Path.Combine(vault, ".vaultdelta.lock")));
    }

    [Fact]
    public async Task Zip_package_is_staged_verified_and_applied()
    {
        string vault = Path.Combine(_root, "zip-vault");
        string source = Path.Combine(_root, "zip-source");
        string zip = Path.Combine(_root, "patch.zip");
        Directory.CreateDirectory(vault);
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "note.md"), "from zip", CancellationToken.None);
        FileFingerprint fingerprint = await FingerprintAsync(Path.Combine(source, "note.md"));
        PatchManifest manifest = Manifest(
            "zip-patch",
            PatchOperation.Add(10, RelativePath.Parse("note.md"), fingerprint));
        PackageInspector zipInspector = new(new ZipPackageReader());
        await new ZipPackageWriter(new DirectoryPackageWriter(_hasher), zipInspector)
            .WriteAsync(manifest, source, zip, CancellationToken.None);
        LocalTargetStateReader stateReader = new(_hasher);
        ApplyWorkflow workflow = new(
            zipInspector,
            new BaselineValidator(stateReader),
            new PatchPayloadStager(_hasher),
            new JsonApplyJournalStore(),
            new TargetLockManager(),
            new BackupStore(_hasher),
            new AtomicFileWriter(_hasher),
            new LocalApplyFileOperations(),
            stateReader,
            new PlatformCapabilityProbe());

        ApplyResult result = await workflow.ApplyAsync(
            new ApplyRequest(zip, vault, Path.Combine(_root, "zip-transactions"), "zip"),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("from zip", await File.ReadAllTextAsync(Path.Combine(vault, "note.md"), CancellationToken.None));
    }

    [Fact]
    public async Task Missing_backup_blocks_rollback_without_guessing()
    {
        string vault = Path.Combine(_root, "missing-backup-vault");
        string source = Path.Combine(_root, "missing-backup-source");
        string package = Path.Combine(_root, "missing-backup-package");
        string transactions = Path.Combine(_root, "missing-backup-transactions");
        Directory.CreateDirectory(vault);
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(vault, "note.md"), "before", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(source, "note.md"), "after", CancellationToken.None);
        PatchManifest manifest = Manifest(
            "missing-backup-patch",
            PatchOperation.Modify(
                10,
                RelativePath.Parse("note.md"),
                await FingerprintAsync(Path.Combine(vault, "note.md")),
                await FingerprintAsync(Path.Combine(source, "note.md"))));
        await new DirectoryPackageWriter(_hasher).WriteAsync(manifest, source, package, CancellationToken.None);
        ApplyResult applied = await CreateApplyWorkflow().ApplyAsync(
            new ApplyRequest(package, vault, transactions, "missing-backup"),
            CancellationToken.None);
        Assert.True(applied.Succeeded, applied.Error);
        ApplyJournal journal = await new JsonApplyJournalStore().LoadAsync(applied.JournalPath!, CancellationToken.None);
        File.Delete(Path.Combine(journal.BackupRoot, journal.Operations[0].BackupRelativePath!.Replace('/', Path.DirectorySeparatorChar)));

        RollbackResult rollback = await CreateRollbackWorkflow().RollbackAsync(applied.JournalPath!, CancellationToken.None);

        Assert.False(rollback.Succeeded);
        Assert.Equal(ApplyJournalStatus.RollingBack, rollback.Status);
        Assert.Equal("after", await File.ReadAllTextAsync(Path.Combine(vault, "note.md"), CancellationToken.None));
    }

    [Fact]
    public async Task Unsupported_filesystem_capability_blocks_before_lock_journal_or_target_write()
    {
        string vault = Path.Combine(_root, "unsupported-vault");
        string source = Path.Combine(_root, "unsupported-source");
        string package = Path.Combine(_root, "unsupported-package");
        string transactions = Path.Combine(_root, "unsupported-transactions");
        Directory.CreateDirectory(vault);
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "note.md"), "new", CancellationToken.None);
        PatchManifest manifest = Manifest(
            "unsupported-patch",
            PatchOperation.Add(
                10,
                RelativePath.Parse("note.md"),
                await FingerprintAsync(Path.Combine(source, "note.md"))));
        await new DirectoryPackageWriter(_hasher).WriteAsync(manifest, source, package, CancellationToken.None);

        ApplyWorkflow workflow = CreateApplyWorkflow(capabilityValidator: new RejectingCapabilityValidator());

        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
            await workflow.ApplyAsync(
                new ApplyRequest(package, vault, transactions, "unsupported"),
                CancellationToken.None));
        Assert.Empty(Directory.GetFileSystemEntries(vault));
        Assert.False(Directory.Exists(transactions));
    }

    private ApplyWorkflow CreateApplyWorkflow(
        IApplyFaultInjector? faultInjector = null,
        IApplyCapabilityValidator? capabilityValidator = null)
    {
        LocalTargetStateReader stateReader = new(_hasher);
        return new ApplyWorkflow(
            new PackageInspector(new DirectoryPackageReader(_hasher)),
            new BaselineValidator(stateReader),
            new PatchPayloadStager(_hasher),
            new JsonApplyJournalStore(),
            new TargetLockManager(),
            new BackupStore(_hasher),
            new AtomicFileWriter(_hasher),
            new LocalApplyFileOperations(),
            stateReader,
            capabilityValidator ?? new PlatformCapabilityProbe(),
            faultInjector);
    }

    private RollbackWorkflow CreateRollbackWorkflow(IApplyFaultInjector? faultInjector = null) =>
        new(
            new JsonApplyJournalStore(),
            new TargetLockManager(),
            new LocalApplyFileOperations(),
            new LocalTargetStateReader(_hasher),
            new PlatformCapabilityProbe(),
            faultInjector);

    private async Task<PatchManifest> CreateManifestAsync(string vault, string source)
    {
        PatchOperation[] operations =
        [
            PatchOperation.AddDirectory(10, RelativePath.Parse("new-folder")),
            PatchOperation.Add(20, RelativePath.Parse("new-folder/added.md"), await FingerprintAsync(Path.Combine(source, "new-folder", "added.md"))),
            PatchOperation.Modify(30, RelativePath.Parse("modify.md"), await FingerprintAsync(Path.Combine(vault, "modify.md")), await FingerprintAsync(Path.Combine(source, "modify.md"))),
            PatchOperation.Delete(40, RelativePath.Parse("delete.md"), await FingerprintAsync(Path.Combine(vault, "delete.md"))),
            PatchOperation.DeleteDirectory(50, RelativePath.Parse("empty-old")),
            PatchOperation.Rename(60, RelativePath.Parse("rename-old.md"), RelativePath.Parse("rename-new.md"), await FingerprintAsync(Path.Combine(vault, "rename-old.md"))),
        ];
        return Manifest("round-trip-patch", operations);
    }

    private static async Task CreateBaselineAsync(string root)
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "empty-old"));
        await File.WriteAllTextAsync(Path.Combine(root, "modify.md"), "before", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(root, "delete.md"), "delete", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(root, "rename-old.md"), "rename", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(root, "same.md"), "same", CancellationToken.None);
    }

    private static async Task CreateTargetAsync(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "new-folder"));
        await File.WriteAllTextAsync(Path.Combine(root, "new-folder", "added.md"), "added", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(root, "modify.md"), "after", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(root, "rename-new.md"), "rename", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(root, "same.md"), "same", CancellationToken.None);
    }

    private async Task<FileFingerprint> FingerprintAsync(string path)
    {
        FileInfo file = new(path);
        string hash = await _hasher.ComputeSha256Async(path, CancellationToken.None);
        file.Refresh();
        return new FileFingerprint(file.Length, file.LastWriteTimeUtc, hash);
    }

    private static PatchManifest Manifest(string patchId, params PatchOperation[] operations) =>
        new(
            "1.0",
            patchId,
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            "rules-v1",
            "sha256:base",
            "sha256:target",
            new PatchSummary(
                operations.Count(operation => operation.Type == PatchOperationType.Add),
                operations.Count(operation => operation.Type == PatchOperationType.Modify),
                operations.Count(operation => operation.Type == PatchOperationType.Delete),
                operations.Count(operation => operation.Type == PatchOperationType.Rename),
                operations.Where(operation => operation.PayloadPath is not null).Sum(operation => operation.NewFingerprint!.Length)),
            operations);

    private static Dictionary<string, string> ReadTree(string root)
    {
        Dictionary<string, string> entries = new(StringComparer.Ordinal);
        foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            entries[$"D:{Path.GetRelativePath(root, directory).Replace('\\', '/')}"] = string.Empty;
        }

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative == ".vaultdelta.lock")
            {
                continue;
            }

            entries[$"F:{relative}"] = File.ReadAllText(file);
        }

        return entries;
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed class ThrowingFaultInjector(ApplyFaultPoint point, int? sequence) : IApplyFaultInjector
    {
        private bool _thrown;

        public void ThrowIfRequested(ApplyFaultPoint currentPoint, int? currentSequence = null)
        {
            if (!_thrown && currentPoint == point && currentSequence == sequence)
            {
                _thrown = true;
                throw new InjectedFailureException($"Injected failure at {point}/{sequence}.");
            }
        }
    }

    private sealed class InjectedFailureException(string message) : Exception(message);

    private sealed class RejectingCapabilityValidator : IApplyCapabilityValidator
    {
        public ValueTask ValidateAsync(
            string targetRoot,
            string transactionRoot,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new PlatformNotSupportedException("Injected unsupported filesystem."));
    }
}
