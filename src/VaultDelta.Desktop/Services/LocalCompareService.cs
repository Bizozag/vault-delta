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
        bool skipLinks = false,
        CancellationToken cancellationToken = default) =>
        _workflow.RunAsync(
            new CompareRequest(baselinePath, targetPath, _rules, skipLinks),
            progress is null ? null : new ThrottledProgress(progress),
            cancellationToken);

    private sealed class ThrottledProgress(IProgress<CompareProgress> target) : IProgress<CompareProgress>
    {
        private long _lastTimestamp;
        private CompareStage? _lastStage;

        public void Report(CompareProgress value)
        {
            long now = Environment.TickCount64;
            if (value.Stage != _lastStage || now - _lastTimestamp >= 100)
            {
                _lastTimestamp = now;
                _lastStage = value.Stage;
                target.Report(value);
            }
        }
    }
}
