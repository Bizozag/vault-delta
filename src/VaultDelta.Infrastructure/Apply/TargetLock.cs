namespace VaultDelta.Infrastructure.Apply;

public sealed class TargetLock : IAsyncDisposable
{
    private readonly FileStream _stream;
    private bool _disposed;

    internal TargetLock(string path, string operationId, FileStream stream)
    {
        Path = path;
        OperationId = operationId;
        _stream = stream;
    }

    public string Path { get; }

    public string OperationId { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _stream.DisposeAsync().ConfigureAwait(false);
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }

        GC.SuppressFinalize(this);
    }
}
