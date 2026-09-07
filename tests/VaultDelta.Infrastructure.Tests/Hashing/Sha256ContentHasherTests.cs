using System.Security.Cryptography;
using System.Text;
using VaultDelta.Infrastructure.Hashing;

namespace VaultDelta.Infrastructure.Tests.Hashing;

public sealed class Sha256ContentHasherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vaultdelta-hash-{Guid.NewGuid():N}");

    public Sha256ContentHasherTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ComputeSha256Async_hashes_an_empty_file()
    {
        string path = Path.Combine(_root, "empty.bin");
        await File.WriteAllBytesAsync(path, [], CancellationToken.None);
        Sha256ContentHasher hasher = new();

        string actual = await hasher.ComputeSha256Async(path, CancellationToken.None);

        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            actual);
    }

    [Fact]
    public async Task ComputeSha256Async_matches_the_framework_for_unicode_content()
    {
        string path = Path.Combine(_root, "笔记.md");
        byte[] content = Encoding.UTF8.GetBytes("Vault Delta\n中文 😀\n");
        await File.WriteAllBytesAsync(path, content, CancellationToken.None);
        Sha256ContentHasher hasher = new();

        string actual = await hasher.ComputeSha256Async(path, CancellationToken.None);

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), actual);
    }

    [Fact]
    public async Task ComputeSha256Async_streams_a_file_larger_than_the_internal_buffer()
    {
        string path = Path.Combine(_root, "large.bin");
        byte[] content = new byte[(256 * 1024) + 17];
        Random.Shared.NextBytes(content);
        await File.WriteAllBytesAsync(path, content, CancellationToken.None);
        Sha256ContentHasher hasher = new(bufferSize: 64 * 1024);

        string actual = await hasher.ComputeSha256Async(path, CancellationToken.None);

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), actual);
    }

    [Fact]
    public async Task ComputeSha256Async_honors_a_pre_cancelled_token()
    {
        string path = Path.Combine(_root, "cancel.bin");
        await File.WriteAllBytesAsync(path, new byte[1024], CancellationToken.None);
        Sha256ContentHasher hasher = new();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await hasher.ComputeSha256Async(path, cancellation.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_non_positive_buffer_sizes(int bufferSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Sha256ContentHasher(bufferSize));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
