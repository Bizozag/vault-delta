using VaultDelta.Application.Abstractions;
using VaultDelta.Application.Apply;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Tests.Apply;

public sealed class BaselineValidatorTests
{
    [Fact]
    public async Task ValidateAsync_passes_when_all_affected_paths_match()
    {
        FileFingerprint oldFingerprint = Fingerprint('a');
        FileFingerprint newFingerprint = Fingerprint('b');
        PatchManifest manifest = Manifest(
            PatchOperation.Modify(10, Path("modify.md"), oldFingerprint, newFingerprint),
            PatchOperation.Delete(20, Path("delete.md"), oldFingerprint),
            PatchOperation.Rename(30, Path("old.md"), Path("new.md"), oldFingerprint),
            PatchOperation.Add(40, Path("added.md"), newFingerprint));
        FakeTargetStateReader reader = new(
            new Dictionary<string, TargetEntryState>
            {
                ["modify.md"] = TargetEntryState.File(oldFingerprint),
                ["delete.md"] = TargetEntryState.File(oldFingerprint),
                ["old.md"] = TargetEntryState.File(oldFingerprint),
            });

        BaselineValidationResult result = await new BaselineValidator(reader)
            .ValidateAsync(manifest, "/vault", CancellationToken.None);

        Assert.Equal(BaselineValidationStatus.Pass, result.Status);
        Assert.Empty(result.Conflicts);
    }

    [Theory]
    [InlineData("missing", BaselineConflictType.MissingExpected)]
    [InlineData("content", BaselineConflictType.UnexpectedContent)]
    [InlineData("type", BaselineConflictType.UnexpectedType)]
    [InlineData("occupied-target", BaselineConflictType.UnexpectedExisting)]
    [InlineData("unreadable", BaselineConflictType.Unreadable)]
    public async Task ValidateAsync_reports_conflicts(string scenario, BaselineConflictType expectedType)
    {
        FileFingerprint oldFingerprint = Fingerprint('a');
        PatchManifest manifest = scenario == "occupied-target"
            ? Manifest(PatchOperation.Add(10, Path("note.md"), Fingerprint('b')))
            : Manifest(PatchOperation.Modify(10, Path("note.md"), oldFingerprint, Fingerprint('b')));
        TargetEntryState state = scenario switch
        {
            "missing" => TargetEntryState.Missing,
            "content" => TargetEntryState.File(Fingerprint('c')),
            "type" => TargetEntryState.Directory,
            "occupied-target" => TargetEntryState.File(oldFingerprint),
            "unreadable" => TargetEntryState.Unreadable("access denied"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        FakeTargetStateReader reader = new(new Dictionary<string, TargetEntryState> { ["note.md"] = state });

        BaselineValidationResult result = await new BaselineValidator(reader)
            .ValidateAsync(manifest, "/vault", CancellationToken.None);

        Assert.Equal(BaselineValidationStatus.Conflict, result.Status);
        Assert.Equal(expectedType, Assert.Single(result.Conflicts).Type);
    }

    [Fact]
    public async Task ValidateAsync_checks_both_sides_of_a_rename()
    {
        FileFingerprint fingerprint = Fingerprint('a');
        PatchManifest manifest = Manifest(PatchOperation.Rename(10, Path("old.md"), Path("new.md"), fingerprint));
        FakeTargetStateReader reader = new(
            new Dictionary<string, TargetEntryState>
            {
                ["old.md"] = TargetEntryState.File(fingerprint),
                ["new.md"] = TargetEntryState.File(Fingerprint('b')),
            });

        BaselineValidationResult result = await new BaselineValidator(reader)
            .ValidateAsync(manifest, "/vault", CancellationToken.None);

        Assert.Equal(BaselineConflictType.UnexpectedExisting, Assert.Single(result.Conflicts).Type);
        Assert.Equal("new.md", result.Conflicts[0].Path.Value);
    }

    [Fact]
    public async Task ValidateAsync_ignores_timestamp_only_differences()
    {
        FileFingerprint expected = new(5, DateTimeOffset.UnixEpoch, new string('a', 64));
        FileFingerprint actual = new(5, DateTimeOffset.UnixEpoch.AddDays(1), new string('a', 64));
        PatchManifest manifest = Manifest(PatchOperation.Delete(10, Path("note.md"), expected));
        FakeTargetStateReader reader = new(
            new Dictionary<string, TargetEntryState> { ["note.md"] = TargetEntryState.File(actual) });

        BaselineValidationResult result = await new BaselineValidator(reader)
            .ValidateAsync(manifest, "/vault", CancellationToken.None);

        Assert.Equal(BaselineValidationStatus.Pass, result.Status);
    }

    private static PatchManifest Manifest(params PatchOperation[] operations) =>
        new(
            "1.0",
            "patch-001",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            "rules-v1",
            "sha256:base",
            "sha256:target",
            new PatchSummary(
                operations.Count(x => x.Type == PatchOperationType.Add),
                operations.Count(x => x.Type == PatchOperationType.Modify),
                operations.Count(x => x.Type == PatchOperationType.Delete),
                operations.Count(x => x.Type == PatchOperationType.Rename),
                operations.Where(x => x.PayloadPath is not null).Sum(x => x.NewFingerprint!.Length)),
            operations);

    private static RelativePath Path(string value) => RelativePath.Parse(value);

    private static FileFingerprint Fingerprint(char hash) =>
        new(5, DateTimeOffset.UnixEpoch, new string(hash, 64));

    private sealed class FakeTargetStateReader(IReadOnlyDictionary<string, TargetEntryState> states)
        : ITargetStateReader
    {
        public ValueTask<TargetEntryState> ReadAsync(
            string targetRoot,
            RelativePath relativePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                states.TryGetValue(relativePath.Value, out TargetEntryState? state)
                    ? state
                    : TargetEntryState.Missing);
        }
    }
}
