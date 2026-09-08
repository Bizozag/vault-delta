using VaultDelta.Application.Compare;
using VaultDelta.Domain.Rules;

namespace VaultDelta.Desktop.Services;

public sealed class LocalCompareService(CompareWorkflow workflow, SnapshotRuleSet rules) : ICompareService
{
    private readonly CompareWorkflow _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
    private readonly SnapshotRuleSet _rules = rules ?? throw new ArgumentNullException(nameof(rules));

    public ValueTask<CompareResult> CompareAsync(
        string baselinePath,
        string targetPath,
        IProgress<CompareProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        _workflow.RunAsync(
            new CompareRequest(baselinePath, targetPath, _rules),
            progress,
            cancellationToken);
}
