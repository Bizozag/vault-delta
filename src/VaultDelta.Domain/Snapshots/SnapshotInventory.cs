using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using VaultDelta.Domain.Paths;

namespace VaultDelta.Domain.Snapshots;

public sealed class SnapshotInventory
{
    private SnapshotInventory(
        string rulesId,
        IReadOnlyList<SnapshotEntry> entries,
        string snapshotId)
    {
        RulesId = rulesId;
        Entries = entries;
        SnapshotId = snapshotId;
    }

    public string RulesId { get; }

    public IReadOnlyList<SnapshotEntry> Entries { get; }

    public string SnapshotId { get; }

    public static SnapshotInventory Create(string rulesId, IEnumerable<SnapshotEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesId);
        ArgumentNullException.ThrowIfNull(entries);

        SnapshotEntry[] orderedEntries = entries
            .OrderBy(entry => entry.Path.Value, StringComparer.Ordinal)
            .ToArray();

        HashSet<RelativePath> portablePaths = new(RelativePath.PortableComparer);
        foreach (SnapshotEntry entry in orderedEntries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!portablePaths.Add(entry.Path))
            {
                throw new ArgumentException(
                    $"Snapshot contains a duplicate or non-portable path collision: {entry.Path}.",
                    nameof(entries));
            }
        }

        string snapshotId = ComputeSnapshotId(rulesId, orderedEntries);
        return new SnapshotInventory(
            rulesId,
            new ReadOnlyCollection<SnapshotEntry>(orderedEntries),
            snapshotId);
    }

    private static string ComputeSnapshotId(string rulesId, IReadOnlyList<SnapshotEntry> entries)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, rulesId);

        foreach (SnapshotEntry entry in entries)
        {
            AppendString(hash, entry.Path.Value);
            AppendInt32(hash, (int)entry.Kind);

            if (entry.Fingerprint is null)
            {
                AppendInt64(hash, 0);
                AppendString(hash, string.Empty);
                continue;
            }

            AppendInt64(hash, entry.Fingerprint.Length);
            AppendString(hash, entry.Fingerprint.Sha256);
        }

        return $"sha256:{Convert.ToHexStringLower(hash.GetHashAndReset())}";
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
