using VaultDelta.Application.Abstractions;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Infrastructure.FileSystem;

public sealed class AtomicFileWriter(IContentHasher contentHasher) : IAtomicFileWriter
{
    private readonly IContentHasher _contentHasher =
        contentHasher ?? throw new ArgumentNullException(nameof(contentHasher));

    public async ValueTask WriteVerifiedAsync(
        string payloadPath,
        string targetPath,
        FileFingerprint expectedFingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(expectedFingerprint);

        string canonicalPayload = Path.GetFullPath(payloadPath);
        string canonicalTarget = Path.GetFullPath(targetPath);
        string? targetDirectory = Path.GetDirectoryName(canonicalTarget);
        if (string.IsNullOrEmpty(targetDirectory))
        {
            throw new ArgumentException("Target path must have a parent directory.", nameof(targetPath));
        }

        Directory.CreateDirectory(targetDirectory);
        string temporaryPath = Path.Combine(targetDirectory, $".vaultdelta-{Guid.NewGuid():N}.tmp");

        try
        {
            await CopyFileAsync(canonicalPayload, temporaryPath, cancellationToken).ConfigureAwait(false);
            await VerifyAsync(temporaryPath, expectedFingerprint, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, canonicalTarget, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private async ValueTask VerifyAsync(
        string path,
        FileFingerprint expectedFingerprint,
        CancellationToken cancellationToken)
    {
        FileInfo info = new(path);
        string actualHash = await _contentHasher.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        if (info.Length != expectedFingerprint.Length
            || !StringComparer.Ordinal.Equals(actualHash, expectedFingerprint.Sha256))
        {
            throw new InvalidDataException($"Temporary file verification failed: {path}.");
        }
    }

    private static async ValueTask CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        const int BufferSize = 1024 * 1024;
        await using FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await source.CopyToAsync(destination, BufferSize, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
