using VaultDelta.Infrastructure.Platform;

namespace VaultDelta.EndToEnd.Tests.Platform;

public sealed class ExternalVolumeCapabilityTests : IDisposable
{
    private string? _probeRoot;

    [Fact]
    public async Task User_supplied_external_volume_is_probed_with_real_filesystem_operations()
    {
        string? externalRoot = Environment.GetEnvironmentVariable("VAULTDELTA_EXTERNAL_VOLUME_PATH");
        if (string.IsNullOrWhiteSpace(externalRoot))
        {
            return;
        }

        _probeRoot = Path.Combine(externalRoot, $"vaultdelta-external-probe-{Guid.NewGuid():N}");
        string target = Path.Combine(_probeRoot, "target");
        string transactions = Path.Combine(_probeRoot, "transactions");
        Directory.CreateDirectory(target);

        PlatformCapabilities result = await new PlatformCapabilityProbe().ProbeAsync(target, transactions);

        Assert.True(result.IsSafeForApply, string.Join(Environment.NewLine, result.Issues));
        Assert.True(result.SameVolumeDirectoryMove);
        Assert.True(result.AtomicFileReplace);
        Assert.True(result.TargetRootLinkFree);
    }

    public void Dispose()
    {
        if (_probeRoot is not null && Directory.Exists(_probeRoot))
        {
            Directory.Delete(_probeRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
