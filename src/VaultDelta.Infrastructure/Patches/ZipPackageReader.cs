using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using VaultDelta.Application.Abstractions;

namespace VaultDelta.Infrastructure.Patches;

public sealed class ZipPackageReader : IPatchPackageReader
{
    public async ValueTask<PatchPackageContent> ReadAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        cancellationToken.ThrowIfCancellationRequested();

        await using FileStream stream = new(
            packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: false);
        List<PatchPackageEntry> entries = [];
        byte[]? manifestBytes = null;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            string path = entry.FullName.Replace('\\', '/');
            ValidateEntryPath(path);
            await using Stream entryStream = entry.Open();

            if (StringComparer.Ordinal.Equals(path, "manifest.json"))
            {
                using MemoryStream manifest = new();
                await entryStream.CopyToAsync(manifest, cancellationToken).ConfigureAwait(false);
                manifestBytes = manifest.ToArray();
                entries.Add(new PatchPackageEntry(path, manifestBytes.LongLength, Hash(manifestBytes)));
                continue;
            }

            (long length, string sha256) = await HashAsync(entryStream, cancellationToken).ConfigureAwait(false);
            entries.Add(new PatchPackageEntry(path, length, sha256));
        }

        if (manifestBytes is null)
        {
            throw new InvalidDataException("ZIP package is missing manifest.json.");
        }

        return new PatchPackageContent(PatchManifestJson.Deserialize(manifestBytes), entries);
    }

    private static void ValidateEntryPath(string path)
    {
        if (path.StartsWith('/')
            || path.Contains("//", StringComparison.Ordinal)
            || path.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"ZIP entry path is unsafe: {path}.");
        }
    }

    private static async ValueTask<(long Length, string Sha256)> HashAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        const int BufferSize = 1024 * 1024;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long length = 0;
            while (true)
            {
                int bytesRead = await stream
                    .ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    return (length, Convert.ToHexStringLower(hash.GetHashAndReset()));
                }

                length += bytesRead;
                hash.AppendData(buffer, 0, bytesRead);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}
