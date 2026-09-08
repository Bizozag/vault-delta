using System.Globalization;
using System.Diagnostics;

namespace VaultDelta.EndToEnd.Tests.Performance;

internal sealed class SyntheticVaultGenerator
{
    private static readonly DateTime FixedWriteTimeUtc = new(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);

    public static async Task<SyntheticVaultScenario> CreateAsync(
        string root,
        int fileCount,
        int changedFileCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fileCount);

        if (changedFileCount < 0 || changedFileCount * 3 > fileCount)
        {
            throw new ArgumentOutOfRangeException(nameof(changedFileCount));
        }

        string baseline = Path.Combine(root, "baseline");
        string target = Path.Combine(root, "target");
        Directory.CreateDirectory(baseline);
        Directory.CreateDirectory(target);
        foreach (int directory in Enumerable.Range(0, Math.Min(fileCount, 100)))
        {
            Directory.CreateDirectory(Path.Combine(baseline, "Notes", $"{directory:D2}"));
            Directory.CreateDirectory(Path.Combine(target, "Notes", $"{directory:D2}"));
        }

        Stopwatch generation = Stopwatch.StartNew();
        ParallelOptions options = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Min(16, Math.Max(4, Environment.ProcessorCount * 2)),
        };

        await Parallel.ForAsync(0, fileCount, options, (index, token) =>
        {
            token.ThrowIfCancellationRequested();
            string relative = Relative(index);
            string content = Content(index, "stable");
            Write(baseline, relative, content);
            Write(target, relative, content);
            return ValueTask.CompletedTask;
        });

        for (int index = 0; index < changedFileCount; index++)
        {
            Write(target, Relative(index), Content(index, "modified"));
            File.Delete(Path.Combine(target, Relative(fileCount - 1 - index).Replace('/', Path.DirectorySeparatorChar)));
            Write(target, $"Added/new-{index:D6}.md", Content(index, "added"));
        }

        Write(baseline, ".trash/ignored.md", "baseline trash");
        Write(target, ".trash/ignored.md", "target trash");
        Write(baseline, ".obsidian/plugins/perf/main.js", "baseline plugin");
        Write(target, ".obsidian/plugins/perf/main.js", "target plugin");
        generation.Stop();
        return new SyntheticVaultScenario(baseline, target, fileCount, changedFileCount, generation.Elapsed);
    }

    private static string Relative(int index) =>
        $"Notes/{index % 100:D2}/{index % 10:D1}/note-{index:D6}.md";

    private static string Content(int index, string state) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"---\nid: {index}\nstate: {state}\n---\n# Note {index}\n[[Index]]\n");

    private static void Write(
        string root,
        string relative,
        string content)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, FixedWriteTimeUtc);
    }
}

internal sealed record SyntheticVaultScenario(
    string BaselineRoot,
    string TargetRoot,
    int FileCount,
    int ChangedFileCount,
    TimeSpan GenerationDuration);
