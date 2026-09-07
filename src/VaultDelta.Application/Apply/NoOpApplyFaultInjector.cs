namespace VaultDelta.Application.Apply;

public sealed class NoOpApplyFaultInjector : IApplyFaultInjector
{
    public static NoOpApplyFaultInjector Instance { get; } = new();

    private NoOpApplyFaultInjector()
    {
    }

    public void ThrowIfRequested(ApplyFaultPoint point, int? sequence = null)
    {
    }
}
