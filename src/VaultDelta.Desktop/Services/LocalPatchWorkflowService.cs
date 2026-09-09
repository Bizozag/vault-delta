using VaultDelta.Application.Apply;
using VaultDelta.Application.Compare;
using VaultDelta.Application.Patches;
using VaultDelta.Domain.Patches;

namespace VaultDelta.Desktop.Services;

public sealed class LocalPatchWorkflowService(
    PackageBuilder builder,
    PackageInspector inspector,
    BaselineValidator baselineValidator,
    ApplyWorkflow applyWorkflow,
    RollbackWorkflow rollbackWorkflow) : IPatchWorkflowService
{
    private readonly PackageBuilder _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    private readonly PackageInspector _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    private readonly BaselineValidator _baselineValidator = baselineValidator ?? throw new ArgumentNullException(nameof(baselineValidator));
    private readonly ApplyWorkflow _applyWorkflow = applyWorkflow ?? throw new ArgumentNullException(nameof(applyWorkflow));
    private readonly RollbackWorkflow _rollbackWorkflow = rollbackWorkflow ?? throw new ArgumentNullException(nameof(rollbackWorkflow));

    public ValueTask BuildAsync(
        CompareResult comparison,
        string sourceRoot,
        string outputPath,
        IProgress<PackageBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        string patchId = $"vault-delta-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{comparison.Target.SnapshotId[^8..]}";
        PatchManifest manifest = PatchManifest.FromDiff(
            patchId,
            DateTimeOffset.UtcNow,
            "0.1.0",
            comparison.Baseline,
            comparison.Target,
            comparison.Differences);
        return _builder.BuildAsync(manifest, sourceRoot, outputPath, progress, cancellationToken);
    }

    public ValueTask<PackageInspectionResult> InspectAsync(string packagePath, CancellationToken cancellationToken = default) =>
        _inspector.InspectAsync(packagePath, cancellationToken);

    public ValueTask<BaselineValidationResult> ValidateAsync(
        PackageInspectionResult inspection,
        string targetRoot,
        CancellationToken cancellationToken = default) =>
        _baselineValidator.ValidateAsync(inspection.Manifest, targetRoot, cancellationToken);

    public ValueTask<ApplyResult> ApplyAsync(
        string packagePath,
        string targetRoot,
        CancellationToken cancellationToken = default) =>
        _applyWorkflow.ApplyAsync(
            new ApplyRequest(packagePath, targetRoot, GetTransactionRoot(targetRoot)),
            cancellationToken);

    public ValueTask<RollbackResult> RollbackAsync(string journalPath, CancellationToken cancellationToken = default) =>
        _rollbackWorkflow.RollbackAsync(journalPath, cancellationToken);

    internal static string GetTransactionRoot(string targetRoot)
    {
        string canonicalTarget = Path.GetFullPath(targetRoot);
        string? parent = Path.GetDirectoryName(canonicalTarget);
        if (string.IsNullOrEmpty(parent))
        {
            throw new ArgumentException("Target root must have a parent directory.", nameof(targetRoot));
        }

        return Path.Combine(parent, $".{Path.GetFileName(canonicalTarget)}.vaultdelta-transactions");
    }
}
