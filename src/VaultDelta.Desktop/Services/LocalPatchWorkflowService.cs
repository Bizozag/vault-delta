using VaultDelta.Application.Apply;
using VaultDelta.Application.Compare;
using VaultDelta.Application.Patches;
using VaultDelta.Domain.Patches;
using System.Text.Json;

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

    public async ValueTask BuildAsync(
        CompareResult comparison,
        string sourceRoot,
        string outputPath,
        IProgress<PackageBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        string canonicalOutput = Path.GetFullPath(outputPath);
        string sourceRelativeOutput = Path.GetRelativePath(Path.GetFullPath(sourceRoot), canonicalOutput);
        if (sourceRelativeOutput != ".." && !sourceRelativeOutput.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(sourceRelativeOutput))
        {
            throw new ArgumentException("更新包不能保存在新快照内部。", nameof(outputPath));
        }
        if (File.Exists(canonicalOutput) || Directory.Exists(canonicalOutput))
        {
            throw new IOException($"Patch output already exists: {canonicalOutput}");
        }
        string reportPath = canonicalOutput + ".excluded-links.json";
        if (comparison.ExcludedLinkPaths.Count > 0 && (File.Exists(reportPath) || Directory.Exists(reportPath)))
        {
            throw new IOException($"链接排除清单已存在，请选择新的输出名称：{reportPath}");
        }
        string patchId = $"vault-delta-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{comparison.Target.SnapshotId[^8..]}";
        PatchManifest manifest = PatchManifest.FromDiff(
            patchId,
            DateTimeOffset.UtcNow,
            GetGeneratorVersion(),
            comparison.Baseline,
            comparison.Target,
            comparison.Differences);
        if (comparison.ExcludedLinkPaths.Count > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(canonicalOutput)!);
            await using FileStream report = new(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(report, new
            {
                patchId,
                excludedPaths = comparison.ExcludedLinkPaths,
                note = "这些路径及其子目录在新旧两侧均不纳入比较；更新包不恢复链接关系。",
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await _builder.BuildAsync(manifest, sourceRoot, outputPath, progress, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (comparison.ExcludedLinkPaths.Count > 0)
            {
                File.Delete(reportPath);
            }

            throw;
        }
    }

    private static string GetGeneratorVersion()
    {
        Version? version = typeof(LocalPatchWorkflowService).Assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public ValueTask<PackageInspectionResult> InspectAsync(string packagePath, CancellationToken cancellationToken = default) =>
        _inspector.InspectAsync(packagePath, cancellationToken);

    public ValueTask<BaselineValidationResult> ValidateAsync(
        PackageInspectionResult inspection,
        string targetRoot,
        IProgress<BaselineValidationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        _baselineValidator.ValidateAsync(inspection.Manifest, targetRoot, progress, cancellationToken);

    public ValueTask<ApplyResult> ApplyAsync(
        string packagePath,
        string targetRoot,
        IReadOnlyList<ConflictResolution>? resolutions = null,
        IProgress<TransactionProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        _applyWorkflow.ApplyAsync(
            new ApplyRequest(packagePath, targetRoot, GetTransactionRoot(targetRoot), Resolutions: resolutions),
            progress,
            cancellationToken);

    public ValueTask<RollbackResult> RollbackAsync(
        string journalPath,
        IProgress<TransactionProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        _rollbackWorkflow.RollbackAsync(journalPath, progress, cancellationToken);

    internal static string GetTransactionRoot(string targetRoot)
    {
        string canonicalTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetRoot));
        string? parent = Path.GetDirectoryName(canonicalTarget);
        if (string.IsNullOrEmpty(parent))
        {
            throw new ArgumentException("Target root must have a parent directory.", nameof(targetRoot));
        }

        return Path.Combine(parent, $".{Path.GetFileName(canonicalTarget)}.vaultdelta-transactions");
    }
}
