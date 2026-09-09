using System.Diagnostics;
using VaultDelta.Application.Compare;
using VaultDelta.Application.Snapshots;
using VaultDelta.Domain.Rules;
using VaultDelta.Infrastructure.FileSystem;
using VaultDelta.Infrastructure.Hashing;

namespace VaultDelta.EndToEnd.Tests.Performance;

public sealed class LargeVaultPerformanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vaultdelta-performance-{Guid.NewGuid():N}");

    [Fact]
    public async Task Synthetic_large_vault_completes_with_expected_sparse_diff()
    {
        int fileCount = ReadPositiveEnvironmentVariable("VAULTDELTA_PERF_FILE_COUNT", 1_000);
        int changedFileCount = Math.Max(1, fileCount / 100);
        SyntheticVaultScenario scenario = await SyntheticVaultGenerator
            .CreateAsync(_root, fileCount, changedFileCount);
        CompareWorkflow workflow = new(new SnapshotScanner(new LocalFileSystem(), new Sha256ContentHasher()));
        Stopwatch stopwatch = Stopwatch.StartNew();

        CompareResult result = await workflow.RunAsync(
            new CompareRequest(scenario.BaselineRoot, scenario.TargetRoot, ObsidianDefaultRules.Create()));

        stopwatch.Stop();
        Assert.Equal(changedFileCount + 1, result.Summary.AddedCount);
        Assert.Equal(changedFileCount + 2, result.Summary.ModifiedCount);
        Assert.Equal(changedFileCount, result.Summary.DeletedCount);
        Assert.Equal(0, result.Summary.RenamedCount);
        Assert.Equal((changedFileCount * 2) + 2, result.Summary.TransferFileCount);
        Assert.Contains(result.Differences.Entries, entry =>
            (entry.TargetPath ?? entry.BasePath)!.Value == ".trash/ignored.md"
            && entry.Type == VaultDelta.Domain.Diffs.DiffEntryType.Modified);
        TimeSpan regressionCeiling = fileCount <= 5_000
            ? TimeSpan.FromMinutes(2)
            : TimeSpan.FromMinutes(15);
        Console.WriteLine(
            $"VaultDelta performance: files={fileCount}, changed-per-kind={changedFileCount}, generation-ms={scenario.GenerationDuration.TotalMilliseconds:0}, compare-ms={stopwatch.Elapsed.TotalMilliseconds:0}, entries={result.Differences.Entries.Count}, ceiling-ms={regressionCeiling.TotalMilliseconds:0}");
        Assert.True(
            stopwatch.Elapsed < regressionCeiling,
            $"Synthetic comparison exceeded the regression ceiling: {stopwatch.Elapsed} for {fileCount} files.");
    }

    private static int ReadPositiveEnvironmentVariable(string name, int fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : fallback;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
