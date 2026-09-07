using VaultDelta.Domain.Paths;

namespace VaultDelta.Application.Abstractions;

public interface ITargetStateReader
{
    ValueTask<TargetEntryState> ReadAsync(
        string targetRoot,
        RelativePath relativePath,
        CancellationToken cancellationToken = default);
}
