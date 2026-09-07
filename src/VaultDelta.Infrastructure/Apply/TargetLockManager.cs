using System.Diagnostics;
using System.Text.Json;

namespace VaultDelta.Infrastructure.Apply;

public sealed class TargetLockManager
{
    private const string LockFileName = ".vaultdelta.lock";
    private readonly Func<int, bool> _isProcessActive;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public TargetLockManager()
        : this(IsProcessActive)
    {
    }

    internal TargetLockManager(Func<int, bool> isProcessActive)
    {
        _isProcessActive = isProcessActive ?? throw new ArgumentNullException(nameof(isProcessActive));
    }

    public async ValueTask<TargetLockInspection> InspectAsync(
        string targetRoot,
        CancellationToken cancellationToken = default)
    {
        string lockPath = GetLockPath(targetRoot);
        if (!File.Exists(lockPath))
        {
            return new TargetLockInspection(TargetLockStatus.None, null, null, null);
        }

        try
        {
            await using FileStream stream = new(
                lockPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            LockDocument? document = await JsonSerializer.DeserializeAsync<LockDocument>(
                stream,
                _jsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (document is null || string.IsNullOrWhiteSpace(document.OperationId))
            {
                return new TargetLockInspection(TargetLockStatus.Invalid, null, null, null);
            }

            bool active = _isProcessActive(document.ProcessId);
            return new TargetLockInspection(
                active ? TargetLockStatus.Active : TargetLockStatus.Stale,
                document.OperationId,
                document.ProcessId,
                document.StartedAtUtc);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new TargetLockInspection(TargetLockStatus.Invalid, null, null, null);
        }
    }

    public ValueTask<TargetLock> AcquireAsync(
        string targetRoot,
        string operationId,
        CancellationToken cancellationToken = default) =>
        AcquireAsync(targetRoot, operationId, replaceStale: false, cancellationToken);

    public async ValueTask<TargetLock> AcquireAsync(
        string targetRoot,
        string operationId,
        bool replaceStale,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        Directory.CreateDirectory(targetRoot);
        string lockPath = GetLockPath(targetRoot);

        if (File.Exists(lockPath))
        {
            TargetLockInspection inspection = await InspectAsync(targetRoot, cancellationToken).ConfigureAwait(false);
            if (!replaceStale || inspection.Status != TargetLockStatus.Stale)
            {
                throw new IOException($"Target vault is already locked ({inspection.Status}).");
            }

            File.Delete(lockPath);
        }

        FileStream stream = new(
            lockPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        try
        {
            LockDocument document = new(operationId, Environment.ProcessId, DateTimeOffset.UtcNow);
            await JsonSerializer.SerializeAsync(
                stream,
                document,
                _jsonOptions,
                cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Position = 0;
            return new TargetLock(lockPath, operationId, stream);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }

            throw;
        }
    }

    private static string GetLockPath(string targetRoot) =>
        Path.Combine(Path.GetFullPath(targetRoot), LockFileName);

    private static bool IsProcessActive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed record LockDocument(string OperationId, int ProcessId, DateTimeOffset StartedAtUtc);
}
