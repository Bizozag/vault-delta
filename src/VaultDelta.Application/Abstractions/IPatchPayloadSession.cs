using VaultDelta.Domain.Paths;

namespace VaultDelta.Application.Abstractions;

public interface IPatchPayloadSession : IAsyncDisposable
{
    string GetPayloadPath(RelativePath payloadPath);
}
