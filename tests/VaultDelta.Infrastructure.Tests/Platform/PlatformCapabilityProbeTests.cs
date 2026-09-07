using System.Text;
using VaultDelta.Infrastructure.Platform;

namespace VaultDelta.Infrastructure.Tests.Platform;

public sealed class PlatformCapabilityProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vaultdelta-platform-{Guid.NewGuid():N}");

    public PlatformCapabilityProbeTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ProbeAsync_verifies_required_capabilities_and_removes_probe_artifacts()
    {
        string target = Path.Combine(_root, "target");
        string transactions = Path.Combine(_root, "transactions");
        Directory.CreateDirectory(target);
        PlatformCapabilityProbe probe = new(CurrentSemantics());

        PlatformCapabilities result = await probe.ProbeAsync(target, transactions, CancellationToken.None);

        Assert.True(result.IsSafeForApply, string.Join(Environment.NewLine, result.Issues));
        Assert.True(result.TargetWritable);
        Assert.True(result.TransactionRootWritable);
        Assert.True(result.SameVolumeDirectoryMove);
        Assert.True(result.AtomicFileReplace);
        Assert.True(result.TargetRootLinkFree);
        Assert.Empty(Directory.GetFileSystemEntries(target));
        Assert.False(Directory.Exists(transactions));
        Assert.Equal(OperatingSystem.IsWindows() ? "Windows" : "macOS", result.PlatformName);
    }

    [Fact]
    public async Task ProbeAsync_reports_the_actual_case_and_unicode_behavior_of_the_volume()
    {
        string target = Path.Combine(_root, "semantics-target");
        string transactions = Path.Combine(_root, "semantics-transactions");
        Directory.CreateDirectory(target);
        bool expectedCaseSensitive = DetectCaseSensitivity(target);
        UnicodeFileNameBehavior expectedUnicode = DetectUnicodeBehavior(target);

        PlatformCapabilities result = await new PlatformCapabilityProbe(CurrentSemantics())
            .ProbeAsync(target, transactions, CancellationToken.None);

        Assert.Equal(expectedCaseSensitive, result.CaseSensitive);
        Assert.Equal(expectedUnicode, result.UnicodeFileNames);
    }

    [Fact]
    public void Constructor_rejects_semantics_for_another_platform()
    {
        IFileSystemSemantics wrong = OperatingSystem.IsWindows()
            ? new MacOsFileSystemSemantics()
            : new WindowsFileSystemSemantics();

        Assert.Throws<PlatformNotSupportedException>(() => new PlatformCapabilityProbe(wrong));
    }

    [Fact]
    public void Semantics_detect_a_link_or_reparse_point_when_supported()
    {
        string target = Path.Combine(_root, "link-target");
        string link = Path.Combine(_root, "link");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.True(CurrentSemantics().IsLink(new DirectoryInfo(link)));
    }

    [Theory]
    [InlineData(false, true, true, true, true)]
    [InlineData(true, false, true, true, true)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, true, true, false)]
    public void EnsureSafeForApply_rejects_each_missing_required_capability(
        bool targetWritable,
        bool transactionWritable,
        bool sameVolumeMove,
        bool atomicReplace,
        bool targetRootLinkFree)
    {
        PlatformCapabilities capabilities = new(
            "test",
            targetWritable,
            transactionWritable,
            sameVolumeMove,
            atomicReplace,
            caseSensitive: true,
            UnicodeFileNameBehavior.PreservesDistinctForms,
            targetRootLinkFree,
            ["Injected capability failure."]);

        Assert.Throws<PlatformNotSupportedException>(capabilities.EnsureSafeForApply);
    }

    private static IFileSystemSemantics CurrentSemantics() =>
        OperatingSystem.IsWindows()
            ? new WindowsFileSystemSemantics()
            : new MacOsFileSystemSemantics();

    private static bool DetectCaseSensitivity(string root)
    {
        string id = Guid.NewGuid().ToString("N");
        string path = Path.Combine(root, $"Case-{id}");
        File.WriteAllText(path, "case");
        bool result = !File.Exists(Path.Combine(root, $"case-{id}"));
        File.Delete(path);
        return result;
    }

    private static UnicodeFileNameBehavior DetectUnicodeBehavior(string root)
    {
        string id = Guid.NewGuid().ToString("N");
        string composedName = $"é-{id}".Normalize(NormalizationForm.FormC);
        string decomposedName = composedName.Normalize(NormalizationForm.FormD);
        string path = Path.Combine(root, composedName);
        File.WriteAllText(path, "unicode");
        string observed = Path.GetFileName(Directory.EnumerateFiles(root).Single());
        UnicodeFileNameBehavior result = !StringComparer.Ordinal.Equals(observed, composedName)
            ? UnicodeFileNameBehavior.NormalizesStoredNames
            : File.Exists(Path.Combine(root, decomposedName))
                ? UnicodeFileNameBehavior.TreatsCanonicalFormsAsEquivalent
                : UnicodeFileNameBehavior.PreservesDistinctForms;
        File.Delete(Path.Combine(root, observed));
        return result;
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
