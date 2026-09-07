using VaultDelta.Domain.Patches;

namespace VaultDelta.Application.Abstractions;

public interface IPatchPayloadStager
{
    ValueTask<IPatchPayloadSession> StageAsync(
        string packagePath,
        PatchManifest manifest,
        CancellationToken cancellationToken = default);
}
