namespace VaultDelta.Application.Abstractions;

public interface IContentHasher
{
    ValueTask<string> ComputeSha256Async(
        string fullPath,
        CancellationToken cancellationToken = default);
}
