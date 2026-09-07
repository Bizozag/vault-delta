using VaultDelta.Application.Abstractions;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Apply;

public sealed class BaselineValidator(ITargetStateReader targetStateReader)
{
    private readonly ITargetStateReader _targetStateReader =
        targetStateReader ?? throw new ArgumentNullException(nameof(targetStateReader));

    public async ValueTask<BaselineValidationResult> ValidateAsync(
        PatchManifest manifest,
        string targetRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);

        List<BaselineConflict> conflicts = [];
        foreach (PatchOperation operation in manifest.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (operation.BasePath is not null)
            {
                TargetEntryState source = await _targetStateReader
                    .ReadAsync(targetRoot, operation.BasePath, cancellationToken)
                    .ConfigureAwait(false);
                BaselineConflict? sourceConflict = ValidateExpected(operation.BasePath, operation.EntryKind, operation.OldFingerprint, source);
                if (sourceConflict is not null)
                {
                    conflicts.Add(sourceConflict);
                }
            }

            bool targetMustBeMissing = operation.Type is PatchOperationType.Add or PatchOperationType.Rename;
            if (targetMustBeMissing && operation.TargetPath is not null)
            {
                TargetEntryState target = await _targetStateReader
                    .ReadAsync(targetRoot, operation.TargetPath, cancellationToken)
                    .ConfigureAwait(false);
                if (target.Kind == TargetEntryStateKind.Unreadable)
                {
                    conflicts.Add(new BaselineConflict(
                        operation.TargetPath,
                        BaselineConflictType.Unreadable,
                        target.Error ?? "Target path cannot be inspected."));
                }
                else if (target.Kind != TargetEntryStateKind.Missing)
                {
                    conflicts.Add(new BaselineConflict(
                        operation.TargetPath,
                        BaselineConflictType.UnexpectedExisting,
                        "The operation target already exists."));
                }
            }
        }

        return new BaselineValidationResult(conflicts);
    }

    private static BaselineConflict? ValidateExpected(
        RelativePath path,
        SnapshotEntryKind expectedKind,
        FileFingerprint? expectedFingerprint,
        TargetEntryState actual)
    {
        if (actual.Kind == TargetEntryStateKind.Unreadable)
        {
            return new BaselineConflict(path, BaselineConflictType.Unreadable, actual.Error ?? "Path cannot be inspected.");
        }

        if (actual.Kind == TargetEntryStateKind.Missing)
        {
            return new BaselineConflict(path, BaselineConflictType.MissingExpected, "The expected baseline entry is missing.");
        }

        TargetEntryStateKind expectedStateKind = expectedKind == SnapshotEntryKind.File
            ? TargetEntryStateKind.File
            : TargetEntryStateKind.Directory;
        if (actual.Kind != expectedStateKind)
        {
            return new BaselineConflict(path, BaselineConflictType.UnexpectedType, "The target entry type differs from the baseline.");
        }

        if (expectedKind == SnapshotEntryKind.File
            && (actual.Fingerprint!.Length != expectedFingerprint!.Length
                || !StringComparer.Ordinal.Equals(actual.Fingerprint.Sha256, expectedFingerprint.Sha256)))
        {
            return new BaselineConflict(path, BaselineConflictType.UnexpectedContent, "The target file content differs from the baseline.");
        }

        return null;
    }
}
