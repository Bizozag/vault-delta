namespace VaultDelta.Application.Abstractions;

public interface IPatchPackageReader
{
    ValueTask<PatchPackageContent> ReadAsync(
        string packagePath,
        CancellationToken cancellationToken = default);
}
