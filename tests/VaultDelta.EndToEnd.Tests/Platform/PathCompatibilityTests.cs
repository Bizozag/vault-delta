using System.Text;
using VaultDelta.Application.Snapshots;
using VaultDelta.Domain.Rules;
using VaultDelta.Infrastructure.FileSystem;
using VaultDelta.Infrastructure.Hashing;

namespace VaultDelta.EndToEnd.Tests.Platform;

public sealed class PathCompatibilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vaultdelta-paths-{Guid.NewGuid():N}");

    [Fact]
    public async Task Scanner_handles_a_portable_relative_path_beyond_legacy_windows_max_path()
    {
        string vault = Path.Combine(_root, "long-path");
        string relative = string.Join(
            '/',
            Enumerable.Range(0, 12).Select(index => $"segment-{index:D2}-abcdefghijkl")) + "/note.md";
        string fullPath = Path.Combine(vault, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "long path");

        var inventory = await Scanner().ScanAsync(vault, ObsidianDefaultRules.Create());

        Assert.True(fullPath.Length > 260, $"Fixture path was not long enough: {fullPath.Length}");
        Assert.Contains(inventory.Entries, entry => entry.Path.Value == relative);
    }

    [Fact]
    public async Task Scanner_rejects_case_collisions_when_the_volume_can_store_them()
    {
        string vault = Path.Combine(_root, "case-collision");
        Directory.CreateDirectory(vault);
        string upper = Path.Combine(vault, "Note.md");
        string lower = Path.Combine(vault, "note.md");
        await File.WriteAllTextAsync(upper, "upper");
        await File.WriteAllTextAsync(lower, "lower");
        if (Directory.EnumerateFiles(vault).Count() < 2)
        {
            return;
        }

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Scanner().ScanAsync(vault, ObsidianDefaultRules.Create()));
    }

    [Fact]
    public async Task Scanner_rejects_nfc_nfd_collisions_when_the_volume_can_store_them()
    {
        string vault = Path.Combine(_root, "unicode-collision");
        Directory.CreateDirectory(vault);
        string composed = "Café.md".Normalize(NormalizationForm.FormC);
        string decomposed = "Café.md".Normalize(NormalizationForm.FormD);
        await File.WriteAllTextAsync(Path.Combine(vault, composed), "composed");
        await File.WriteAllTextAsync(Path.Combine(vault, decomposed), "decomposed");
        if (Directory.EnumerateFiles(vault).Select(Path.GetFileName).Distinct(StringComparer.Ordinal).Count() < 2)
        {
            return;
        }

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Scanner().ScanAsync(vault, ObsidianDefaultRules.Create()));
    }

    private static SnapshotScanner Scanner() =>
        new(new LocalFileSystem(), new Sha256ContentHasher());

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
