using VaultDelta.Domain.Apply;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;
using VaultDelta.Infrastructure.Apply;

namespace VaultDelta.Infrastructure.Tests.Apply;

public sealed class JsonApplyJournalStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vaultdelta-journal-{Guid.NewGuid():N}");

    public JsonApplyJournalStoreTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task SaveAsync_round_trips_every_state_transition()
    {
        string path = Path.Combine(_root, "journal.json");
        JsonApplyJournalStore store = new();
        ApplyJournal journal = CreateJournal();

        await store.SaveAsync(path, journal, CancellationToken.None);
        ApplyJournal loaded = await store.LoadAsync(path, CancellationToken.None);
        ApplyJournal applying = loaded.WithStatus(ApplyJournalStatus.Applying);
        await store.SaveAsync(path, applying, CancellationToken.None);
        ApplyJournal reloaded = await store.LoadAsync(path, CancellationToken.None);

        Assert.Equal(journal.OperationId, loaded.OperationId);
        Assert.Equal(ApplyJournalStatus.Applying, reloaded.Status);
        Assert.Equal(journal.Operations[0].Operation, reloaded.Operations[0].Operation);
        Assert.False(File.Exists($"{path}.tmp"));
    }

    [Fact]
    public async Task LoadAsync_rejects_corrupted_json()
    {
        string path = Path.Combine(_root, "bad.json");
        await File.WriteAllTextAsync(path, "{bad", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new JsonApplyJournalStore().LoadAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task TargetLock_rejects_a_second_live_holder()
    {
        TargetLockManager manager = new();
        await using TargetLock first = await manager.AcquireAsync(_root, "operation-001", CancellationToken.None);

        TargetLockInspection inspection = await manager.InspectAsync(_root, CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(async () =>
            await manager.AcquireAsync(_root, "operation-002", CancellationToken.None));

        Assert.Equal(TargetLockStatus.Active, inspection.Status);
        Assert.Equal("operation-001", inspection.OperationId);
    }

    [Fact]
    public async Task TargetLock_reports_a_stale_lock_and_can_replace_it()
    {
        string lockPath = Path.Combine(_root, ".vaultdelta.lock");
        await File.WriteAllTextAsync(
            lockPath,
            "{\"operationId\":\"old\",\"processId\":2147483647,\"startedAtUtc\":\"2026-01-01T00:00:00Z\"}",
            CancellationToken.None);
        TargetLockManager manager = new();

        TargetLockInspection inspection = await manager.InspectAsync(_root, CancellationToken.None);
        await using TargetLock replacement = await manager.AcquireAsync(
            _root,
            "operation-new",
            replaceStale: true,
            CancellationToken.None);

        Assert.Equal(TargetLockStatus.Stale, inspection.Status);
        Assert.Equal("old", inspection.OperationId);
        Assert.Equal("operation-new", replacement.OperationId);
    }

    private ApplyJournal CreateJournal() =>
        ApplyJournal.Create(
            "operation-001",
            "patch-001",
            _root,
            Path.Combine(_root, "backup"),
            [PatchOperation.AddDirectory(10, RelativePath.Parse("Folder"))]);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
