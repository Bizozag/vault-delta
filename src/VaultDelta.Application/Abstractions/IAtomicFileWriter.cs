using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Abstractions;

public interface IAtomicFileWriter
{
    ValueTask WriteVerifiedAsync(
        string payloadPath,
        string targetPath,
        FileFingerprint expectedFingerprint,
        CancellationToken cancellationToken = default);
}
