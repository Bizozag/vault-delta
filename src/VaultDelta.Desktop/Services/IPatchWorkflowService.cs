using VaultDelta.Application.Apply;
using VaultDelta.Application.Compare;
using VaultDelta.Application.Patches;

namespace VaultDelta.Desktop.Services;

public interface IPatchWorkflowService
{
    ValueTask BuildAsync(
        CompareResult comparison,
        string sourceRoot,
        string outputPath,
        IProgress<PackageBuildProgress>? progress = null,
        CancellationToken cancellationToken = default);

    ValueTask<PackageInspectionResult> InspectAsync(
        string packagePath,
        CancellationToken cancellationToken = default);

    ValueTask<BaselineValidationResult> ValidateAsync(
        PackageInspectionResult inspection,
        string targetRoot,
        CancellationToken cancellationToken = default);

    ValueTask<ApplyResult> ApplyAsync(
        string packagePath,
        string targetRoot,
        CancellationToken cancellationToken = default);

    ValueTask<RollbackResult> RollbackAsync(
        string journalPath,
        CancellationToken cancellationToken = default);
}
