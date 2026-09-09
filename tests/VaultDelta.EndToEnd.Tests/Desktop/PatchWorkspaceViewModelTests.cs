using VaultDelta.Application.Apply;
using VaultDelta.Application.Compare;
using VaultDelta.Application.Patches;
using VaultDelta.Desktop.Services;
using VaultDelta.Desktop.ViewModels;
using VaultDelta.Domain.Apply;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;

namespace VaultDelta.EndToEnd.Tests.Desktop;

public sealed class PatchWorkspaceViewModelTests
{
    [Fact]
    public async Task Opening_valid_package_then_passing_baseline_enables_apply()
    {
        FakePatchWorkflow workflow = new()
        {
            Inspection = Inspection(),
            Validation = new BaselineValidationResult([]),
        };
        PatchWorkspaceViewModel viewModel = Create(workflow, patch: "D:/patch.zip", target: "D:/vault");

        await viewModel.OpenPatchAsync();
        await viewModel.SelectTargetAsync();
        await viewModel.ValidateAsync();

        Assert.Equal(PatchWorkspaceState.ReadyToApply, viewModel.State);
        Assert.True(viewModel.CanApply);
        Assert.Equal(4, viewModel.OperationCount);
        Assert.Equal("2 个文件 · 3 KB", viewModel.PayloadText);
    }

    [Fact]
    public async Task Baseline_conflict_is_listed_and_never_enables_apply()
    {
        FakePatchWorkflow workflow = new()
        {
            Inspection = Inspection(),
            Validation = new BaselineValidationResult(
                [new BaselineConflict(RelativePath.Parse("note.md"), BaselineConflictType.UnexpectedContent, "changed")]),
        };
        PatchWorkspaceViewModel viewModel = Create(workflow, patch: "D:/patch.zip", target: "D:/vault");

        await viewModel.OpenPatchAsync();
        await viewModel.SelectTargetAsync();
        await viewModel.ValidateAsync();

        Assert.Equal(PatchWorkspaceState.Conflict, viewModel.State);
        Assert.False(viewModel.CanApply);
        Assert.Single(viewModel.Conflicts);
        Assert.Equal("内容不一致", viewModel.Conflicts[0].TypeText);
        Assert.Equal(0, workflow.ApplyCalls);
    }

    [Fact]
    public async Task Interrupted_apply_exposes_journal_and_recovery_action()
    {
        FakePatchWorkflow workflow = new()
        {
            Inspection = Inspection(),
            Validation = new BaselineValidationResult([]),
            ApplyResult = new ApplyResult(
                ApplyJournalStatus.NeedsRollback,
                new BaselineValidationResult([]),
                "D:/transactions/op/journal.json",
                "interrupted"),
        };
        PatchWorkspaceViewModel viewModel = Create(workflow, patch: "D:/patch.zip", target: "D:/vault");
        await viewModel.OpenPatchAsync();
        await viewModel.SelectTargetAsync();
        await viewModel.ValidateAsync();

        await viewModel.ApplyAsync();

        Assert.Equal(PatchWorkspaceState.NeedsRollback, viewModel.State);
        Assert.Equal("D:/transactions/op/journal.json", viewModel.JournalPath);
        Assert.True(viewModel.CanRollback);
    }

    [Fact]
    public async Task Selected_journal_can_be_rolled_back_without_package()
    {
        FakePatchWorkflow workflow = new()
        {
            RollbackResult = new RollbackResult(ApplyJournalStatus.RolledBack, "D:/journal.json", null),
        };
        PatchWorkspaceViewModel viewModel = Create(workflow, journal: "D:/journal.json");

        await viewModel.OpenJournalAsync();
        await viewModel.RollbackAsync();

        Assert.Equal(PatchWorkspaceState.RolledBack, viewModel.State);
        Assert.Equal(1, workflow.RollbackCalls);
    }

    [Fact]
    public async Task Corrupt_package_shows_error_without_a_package_summary()
    {
        FakePatchWorkflow workflow = new() { InspectionError = new InvalidDataException("bad payload") };
        PatchWorkspaceViewModel viewModel = Create(workflow, patch: "D:/bad.zip");

        await viewModel.OpenPatchAsync();

        Assert.Equal(PatchWorkspaceState.Error, viewModel.State);
        Assert.False(viewModel.HasPackage);
        Assert.Contains("bad payload", viewModel.ErrorMessage);
    }

    private static PatchWorkspaceViewModel Create(
        FakePatchWorkflow workflow,
        string? patch = null,
        string? target = null,
        string? journal = null) =>
        new(
            new QueueFolderPicker(target is null ? [] : [target]),
            new QueueStoragePicker(patch, journal),
            workflow);

    private static PackageInspectionResult Inspection()
    {
        PatchOperation[] operations =
        [
            PatchOperation.Add(10, RelativePath.Parse("add.md"), Fingerprint(1024, 'a')),
            PatchOperation.Modify(20, RelativePath.Parse("edit.md"), Fingerprint(2, 'b'), Fingerprint(2048, 'c')),
            PatchOperation.Delete(30, RelativePath.Parse("delete.md"), Fingerprint(2, 'd')),
            PatchOperation.Rename(40, RelativePath.Parse("old.md"), RelativePath.Parse("new.md"), Fingerprint(2, 'e')),
        ];
        PatchManifest manifest = new(
            "1.0",
            "patch-id",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            "rules",
            "base",
            "target",
            new PatchSummary(1, 1, 1, 1, 3072),
            operations);
        return new PackageInspectionResult(manifest, 2, 3072);
    }

    private static VaultDelta.Domain.Snapshots.FileFingerprint Fingerprint(long length, char hash) =>
        new(length, DateTimeOffset.UnixEpoch, new string(hash, 64));

    private sealed class QueueFolderPicker(params string[] selections) : IFolderPicker
    {
        private readonly Queue<string> _selections = new(selections);
        public Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(_selections.Count == 0 ? null : _selections.Dequeue());
    }

    private sealed class QueueStoragePicker(string? patch, string? journal) : IPatchStoragePicker
    {
        public Task<string?> SavePatchAsync(string suggestedFileName, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> OpenPatchAsync(CancellationToken cancellationToken = default) => Task.FromResult(patch);
        public Task<string?> OpenJournalAsync(CancellationToken cancellationToken = default) => Task.FromResult(journal);
    }

    private sealed class FakePatchWorkflow : IPatchWorkflowService
    {
        public PackageInspectionResult? Inspection { get; init; }
        public Exception? InspectionError { get; init; }
        public BaselineValidationResult Validation { get; init; } = new([]);
        public ApplyResult ApplyResult { get; init; } = new(ApplyJournalStatus.Committed, new BaselineValidationResult([]), "journal.json", null);
        public RollbackResult RollbackResult { get; init; } = new(ApplyJournalStatus.RolledBack, "journal.json", null);
        public int ApplyCalls { get; private set; }
        public int RollbackCalls { get; private set; }

        public ValueTask BuildAsync(CompareResult comparison, string sourceRoot, string outputPath, IProgress<PackageBuildProgress>? progress = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<PackageInspectionResult> InspectAsync(string packagePath, CancellationToken cancellationToken = default) =>
            InspectionError is null
                ? ValueTask.FromResult(Inspection!)
                : ValueTask.FromException<PackageInspectionResult>(InspectionError);

        public ValueTask<BaselineValidationResult> ValidateAsync(PackageInspectionResult inspection, string targetRoot, CancellationToken cancellationToken = default) => ValueTask.FromResult(Validation);

        public ValueTask<ApplyResult> ApplyAsync(string packagePath, string targetRoot, CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            return ValueTask.FromResult(ApplyResult);
        }

        public ValueTask<RollbackResult> RollbackAsync(string journalPath, CancellationToken cancellationToken = default)
        {
            RollbackCalls++;
            return ValueTask.FromResult(RollbackResult);
        }
    }
}
