using VaultDelta.Domain.Snapshots;
using VaultDelta.Infrastructure.FileSystem;
using VaultDelta.Infrastructure.Hashing;

namespace VaultDelta.Infrastructure.Tests.FileSystem;

public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vaultdelta-atomic-{Guid.NewGuid():N}");

    public AtomicFileWriterTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task WriteVerifiedAsync_creates_and_verifies_a_new_file()
    {
        string payload = Path.Combine(_root, "payload.bin");
        string target = Path.Combine(_root, "target", "note.bin");
        await File.WriteAllTextAsync(payload, "after", CancellationToken.None);
        FileFingerprint fingerprint = await FingerprintAsync(payload);
        AtomicFileWriter writer = new(new Sha256ContentHasher());

        await writer.WriteVerifiedAsync(payload, target, fingerprint, CancellationToken.None);

        Assert.Equal("after", await File.ReadAllTextAsync(target, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, ".vaultdelta-*.tmp"));
    }

    [Fact]
    public async Task WriteVerifiedAsync_atomically_replaces_an_existing_file()
    {
        string payload = Path.Combine(_root, "payload-replace.bin");
        string target = Path.Combine(_root, "target-replace.bin");
        await File.WriteAllTextAsync(payload, "after", CancellationToken.None);
        await File.WriteAllTextAsync(target, "before", CancellationToken.None);
        FileFingerprint fingerprint = await FingerprintAsync(payload);
        AtomicFileWriter writer = new(new Sha256ContentHasher());

        await writer.WriteVerifiedAsync(payload, target, fingerprint, CancellationToken.None);

        Assert.Equal("after", await File.ReadAllTextAsync(target, CancellationToken.None));
    }

    [Fact]
    public async Task WriteVerifiedAsync_preserves_existing_target_when_payload_verification_fails()
    {
        string payload = Path.Combine(_root, "bad-payload.bin");
        string target = Path.Combine(_root, "safe-target.bin");
        await File.WriteAllTextAsync(payload, "bad", CancellationToken.None);
        await File.WriteAllTextAsync(target, "before", CancellationToken.None);
        FileFingerprint wrong = new(3, DateTimeOffset.UnixEpoch, new string('a', 64));
        AtomicFileWriter writer = new(new Sha256ContentHasher());

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await writer.WriteVerifiedAsync(payload, target, wrong, CancellationToken.None));

        Assert.Equal("before", await File.ReadAllTextAsync(target, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_root, ".vaultdelta-*.tmp"));
    }

    private static async Task<FileFingerprint> FingerprintAsync(string path)
    {
        FileInfo info = new(path);
        string hash = await new Sha256ContentHasher().ComputeSha256Async(path, CancellationToken.None);
        return new FileFingerprint(info.Length, info.LastWriteTimeUtc, hash);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
