using VaultDelta.Application.Snapshots;
using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Compare;

public sealed class CompareWorkflow(SnapshotScanner scanner)
{
    private readonly SnapshotScanner _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));

    public async ValueTask<CompareResult> RunAsync(
        CompareRequest request,
        IProgress<CompareProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRoots(request.BaselinePath, request.TargetPath);
        ArgumentNullException.ThrowIfNull(request.Rules);

        progress?.Report(new CompareProgress(CompareStage.ScanningBaseline, 0));
        SnapshotInventory baseline = await _scanner
            .ScanAsync(
                request.BaselinePath,
                request.Rules,
                new ForwardingProgress(progress, CompareStage.ScanningBaseline),
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new CompareProgress(CompareStage.ScanningTarget, 0));
        SnapshotInventory target = await _scanner
            .ScanAsync(
                request.TargetPath,
                request.Rules,
                new ForwardingProgress(progress, CompareStage.ScanningTarget),
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new CompareProgress(CompareStage.Comparing, 0));
        DiffSet differences = DiffEngine.Compare(baseline, target);
        CompareSummary summary = Summarize(differences);
        progress?.Report(new CompareProgress(CompareStage.Completed, differences.Entries.Count));
        return new CompareResult(baseline, target, differences, summary);
    }

    private static CompareSummary Summarize(DiffSet differences)
    {
        int Count(DiffEntryType type) => differences.Entries.Count(entry => entry.Type == type);
        DiffEntry[] transferEntries = differences.Entries
            .Where(entry => entry.Type is DiffEntryType.Added or DiffEntryType.Modified)
            .Where(entry => entry.TargetEntry!.Fingerprint is not null)
            .ToArray();

        return new CompareSummary(
            Count(DiffEntryType.Added),
            Count(DiffEntryType.Modified),
            Count(DiffEntryType.Deleted),
            Count(DiffEntryType.Renamed),
            Count(DiffEntryType.Unchanged),
            transferEntries.Length,
            transferEntries.Sum(entry => entry.TargetEntry!.Fingerprint!.Length),
            differences.Entries.Count(entry => DifferenceRiskClassifier.IsRisk(entry, differences)));
    }

    private static void ValidateRoots(string baselinePath, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselinePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string baseline = NormalizeRoot(baselinePath);
        string target = NormalizeRoot(targetPath);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (baseline.Equals(target, comparison))
        {
            throw new ArgumentException("Baseline and target folders must be different.", nameof(targetPath));
        }

        string baselinePrefix = baseline + Path.DirectorySeparatorChar;
        string targetPrefix = target + Path.DirectorySeparatorChar;
        if (baselinePrefix.StartsWith(targetPrefix, comparison)
            || targetPrefix.StartsWith(baselinePrefix, comparison))
        {
            throw new ArgumentException("Baseline and target folders cannot contain one another.", nameof(targetPath));
        }
    }

    private static string NormalizeRoot(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private sealed class ForwardingProgress(
        IProgress<CompareProgress>? progress,
        CompareStage stage) : IProgress<SnapshotScanProgress>
    {
        public void Report(SnapshotScanProgress value) =>
            progress?.Report(new CompareProgress(stage, value.ProcessedEntries, value.CurrentPath));
    }
}
