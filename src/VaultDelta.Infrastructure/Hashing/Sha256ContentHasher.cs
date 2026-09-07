using System.Buffers;
using System.Security.Cryptography;
using VaultDelta.Application.Abstractions;

namespace VaultDelta.Infrastructure.Hashing;

public sealed class Sha256ContentHasher : IContentHasher
{
    public const int DefaultBufferSize = 1024 * 1024;

    private readonly int _bufferSize;

    public Sha256ContentHasher(int bufferSize = DefaultBufferSize)
    {
        if (bufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferSize), bufferSize, "Buffer size must be positive.");
        }

        _bufferSize = bufferSize;
    }

    public async ValueTask<string> ComputeSha256Async(
        string fullPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] buffer = ArrayPool<byte>.Shared.Rent(_bufferSize);
        try
        {
            await using FileStream stream = new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                _bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            while (true)
            {
                int bytesRead = await stream
                    .ReadAsync(buffer.AsMemory(0, _bufferSize), cancellationToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, bytesRead);
            }

            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }
}
