using VaultDelta.Domain.Apply;

namespace VaultDelta.Application.Apply;

public sealed record ApplyResult(
    ApplyJournalStatus? Status,
    BaselineValidationResult Baseline,
    string? JournalPath,
    string? Error)
{
    public bool Succeeded => Status == ApplyJournalStatus.Committed;

    public bool RequiresRollback => Status == ApplyJournalStatus.NeedsRollback;
}
