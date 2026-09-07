using VaultDelta.Domain.Apply;

namespace VaultDelta.Application.Abstractions;

public interface IApplyJournalStore
{
    ValueTask SaveAsync(string journalPath, ApplyJournal journal, CancellationToken cancellationToken = default);

    ValueTask<ApplyJournal> LoadAsync(string journalPath, CancellationToken cancellationToken = default);
}
