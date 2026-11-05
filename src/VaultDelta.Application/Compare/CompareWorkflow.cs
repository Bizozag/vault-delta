using VaultDelta.Application.Snapshots;
using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Snapshots;
using VaultDelta.Domain.Rules;

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

        string[] excludedLinks = [];
        SnapshotRuleSet rules = request.Rules;
        if (request.SkipLinks)
        {
            progress?.Report(new CompareProgress(CompareStage.ScanningBaseline, 0, "正在检查链接"));
            IReadOnlyList<string> baselineLinks = await _scanner.FindLinkedPathsAsync(request.BaselinePath, rules, cancellationToken).ConfigureAwait(false);
            progress?.Report(new CompareProgress(CompareStage.ScanningTarget, 0, "正在检查链接"));
            IReadOnlyList<string> targetLinks = await _scanner.FindLinkedPathsAsync(request.TargetPath, rules, cancellationToken).ConfigureAwait(false);
            excludedLinks = baselineLinks.Concat(targetLinks).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (excludedLinks.Length > 0)
            {
                IEnumerable<SnapshotRule> exclusions = excludedLinks.SelectMany((path, index) => new[]
                {
                    new SnapshotRule($"skip-link-{index}", SnapshotRuleAction.Exclude, path),
                    new SnapshotRule($"skip-link-tree-{index}", SnapshotRuleAction.Exclude, $"{path}/**"),
                });
                rules = SnapshotRuleSet.Create($"{rules.Name}-skip-links-v1", rules.Rules.Concat(exclusions));
            }
        }

        progress?.Report(new CompareProgress(CompareStage.ScanningBaseline, 0));
        SnapshotInventory baseline = await _scanner
            .ScanAsync(
                request.BaselinePath,
                rules,
                new ForwardingProgress(progress, CompareStage.ScanningBaseline),
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new CompareProgress(CompareStage.ScanningTarget, 0));
        SnapshotInventory target = await _scanner
            .ScanAsync(
                request.TargetPath,
                rules,
                new ForwardingProgress(progress, CompareStage.ScanningTarget),
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new CompareProgress(CompareStage.Comparing, 0));
        HashSet<string> targetDirectories = target.Entries
            .Where(entry => entry.Kind == SnapshotEntryKind.Directory)
            .Select(entry => entry.Path.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (SnapshotEntry directory in baseline.Entries.Where(entry => entry.Kind == SnapshotEntryKind.Directory))
        {
            if (!targetDirectories.Contains(directory.Path.Value)
                && excludedLinks.Any(path => path.StartsWith(directory.Path.Value + "/", StringComparison.OrdinalIgnoreCase)))
            {
                throw new SnapshotScanException("跳过的链接位于将被删除或替换的父目录中。请先整理该目录，避免更新时连同链接一起移除。",
                    request.BaselinePath, directory.Path.Value);
            }
        }

        DiffSet differences = DiffEngine.Compare(baseline, target);
        CompareSummary summary = Summarize(differences);
        progress?.Report(new CompareProgress(CompareStage.Completed, differences.Entries.Count));
        return new CompareResult(baseline, target, differences, summary)
        {
            ExcludedLinkPaths = Array.AsReadOnly(excludedLinks),
        };
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
