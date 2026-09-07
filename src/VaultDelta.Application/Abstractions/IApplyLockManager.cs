namespace VaultDelta.Application.Abstractions;

public interface IApplyLockManager
{
    ValueTask<IAsyncDisposable> AcquireAsync(
        string targetRoot,
        string operationId,
        CancellationToken cancellationToken = default);
}
