using VaultDelta.Domain.Apply;

namespace VaultDelta.Application.Apply;

public sealed record RollbackResult(ApplyJournalStatus Status, string JournalPath, string? Error)
{
    public bool Succeeded => Status == ApplyJournalStatus.RolledBack;
}
