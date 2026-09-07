using System.Collections.ObjectModel;

namespace VaultDelta.Infrastructure.Platform;

public sealed class PlatformCapabilities
{
    public PlatformCapabilities(
        string platformName,
        bool targetWritable,
        bool transactionRootWritable,
        bool sameVolumeDirectoryMove,
        bool atomicFileReplace,
        bool caseSensitive,
        UnicodeFileNameBehavior unicodeFileNames,
        bool targetRootLinkFree,
        IReadOnlyList<string> issues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformName);
        ArgumentNullException.ThrowIfNull(issues);
        PlatformName = platformName;
        TargetWritable = targetWritable;
        TransactionRootWritable = transactionRootWritable;
        SameVolumeDirectoryMove = sameVolumeDirectoryMove;
        AtomicFileReplace = atomicFileReplace;
        CaseSensitive = caseSensitive;
        UnicodeFileNames = unicodeFileNames;
        TargetRootLinkFree = targetRootLinkFree;
        Issues = new ReadOnlyCollection<string>(issues.ToArray());
    }

    public string PlatformName { get; }

    public bool TargetWritable { get; }

    public bool TransactionRootWritable { get; }

    public bool SameVolumeDirectoryMove { get; }

    public bool AtomicFileReplace { get; }

    public bool CaseSensitive { get; }

    public UnicodeFileNameBehavior UnicodeFileNames { get; }

    public bool TargetRootLinkFree { get; }

    public IReadOnlyList<string> Issues { get; }

    public bool IsSafeForApply =>
        TargetWritable
        && TransactionRootWritable
        && SameVolumeDirectoryMove
        && AtomicFileReplace
        && TargetRootLinkFree
        && Issues.Count == 0;

    public void EnsureSafeForApply()
    {
        if (!IsSafeForApply)
        {
            string detail = Issues.Count == 0 ? "Required filesystem capabilities are unavailable." : string.Join(" ", Issues);
            throw new PlatformNotSupportedException($"The target filesystem is not safe for transactional apply. {detail}");
        }
    }
}
