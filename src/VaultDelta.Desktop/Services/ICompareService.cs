using VaultDelta.Application.Compare;

namespace VaultDelta.Desktop.Services;

public interface ICompareService
{
    ValueTask<CompareResult> CompareAsync(
        string baselinePath,
        string targetPath,
        IProgress<CompareProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
