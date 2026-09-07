namespace VaultDelta.Application.Apply;

public interface IApplyFaultInjector
{
    void ThrowIfRequested(ApplyFaultPoint point, int? sequence = null);
}
