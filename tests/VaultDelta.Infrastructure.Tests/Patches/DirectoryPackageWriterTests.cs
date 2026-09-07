using System.Text;
using VaultDelta.Application.Patches;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;
using VaultDelta.Infrastructure.Hashing;
using VaultDelta.Infrastructure.Patches;

namespace VaultDelta.Infrastructure.Tests.Patches;

public sealed class DirectoryPackageWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vaultdelta-package-{Guid.NewGuid():N}");

    public DirectoryPackageWriterTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task BuildAsync_publishes_a_verified_directory_package()
    {
        string source = Path.Combine(_root, "source");
        string output = Path.Combine(_root, "patch");
        string sourceFile = Path.Combine(source, "Notes", "new.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "hello", Encoding.UTF8, CancellationToken.None);
        PatchManifest manifest = await CreateAddManifestAsync(source, "Notes/new.md");
        PackageBuilder builder = new(new DirectoryPackageWriter(new Sha256ContentHasher()));

        await builder.BuildAsync(manifest, source, output, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(output, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(output, "README.txt")));
        Assert.Equal(
            "hello",
            await File.ReadAllTextAsync(Path.Combine(output, "files", "Notes", "new.md"), CancellationToken.None));
        PatchManifest restored = PatchManifestJson.Deserialize(
            await File.ReadAllBytesAsync(Path.Combine(output, "manifest.json"), CancellationToken.None));
        Assert.Equal(manifest.PatchId, restored.PatchId);
        Assert.Empty(Directory.GetDirectories(_root, "*.incomplete-*"));
    }

    [Fact]
    public async Task BuildAsync_rejects_an_existing_output_without_modifying_it()
    {
        string source = Path.Combine(_root, "source-existing");
        string output = Path.Combine(_root, "existing-patch");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(output);
        string marker = Path.Combine(output, "keep.txt");
        await File.WriteAllTextAsync(marker, "keep", CancellationToken.None);
        PackageBuilder builder = new(new DirectoryPackageWriter(new Sha256ContentHasher()));

        await Assert.ThrowsAsync<IOException>(async () =>
            await builder.BuildAsync(EmptyManifest(), source, output, CancellationToken.None));

        Assert.Equal("keep", await File.ReadAllTextAsync(marker, CancellationToken.None));
    }

    [Fact]
    public async Task BuildAsync_does_not_publish_when_payload_hash_is_wrong()
    {
        string source = Path.Combine(_root, "source-wrong-hash");
        string output = Path.Combine(_root, "bad-patch");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "note.md"), "actual", CancellationToken.None);
        FileFingerprint wrong = new(6, DateTimeOffset.UnixEpoch, new string('a', 64));
        PatchManifest manifest = ManifestWithOperation(PatchOperation.Add(10, RelativePath.Parse("note.md"), wrong));
        PackageBuilder builder = new(new DirectoryPackageWriter(new Sha256ContentHasher()));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await builder.BuildAsync(manifest, source, output, CancellationToken.None));

        Assert.False(Directory.Exists(output));
        Assert.Empty(Directory.GetDirectories(_root, "*.incomplete-*"));
    }

    [Fact]
    public async Task BuildAsync_rejects_output_inside_the_source_tree()
    {
        string source = Path.Combine(_root, "source-contained");
        Directory.CreateDirectory(source);
        string output = Path.Combine(source, "patch");
        PackageBuilder builder = new(new DirectoryPackageWriter(new Sha256ContentHasher()));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await builder.BuildAsync(EmptyManifest(), source, output, CancellationToken.None));

        Assert.False(Directory.Exists(output));
    }

    private static async Task<PatchManifest> CreateAddManifestAsync(string source, string relativePath)
    {
        string fullPath = Path.Combine(source, relativePath.Replace('/', Path.DirectorySeparatorChar));
        FileInfo info = new(fullPath);
        string hash = await new Sha256ContentHasher().ComputeSha256Async(fullPath, CancellationToken.None);
        FileFingerprint fingerprint = new(info.Length, info.LastWriteTimeUtc, hash);
        return ManifestWithOperation(PatchOperation.Add(10, RelativePath.Parse(relativePath), fingerprint));
    }

    private static PatchManifest ManifestWithOperation(PatchOperation operation) =>
        new(
            "1.0",
            "patch-001",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            "rules-v1",
            "sha256:base",
            "sha256:target",
            new PatchSummary(
                operation.Type == PatchOperationType.Add ? 1 : 0,
                operation.Type == PatchOperationType.Modify ? 1 : 0,
                operation.Type == PatchOperationType.Delete ? 1 : 0,
                operation.Type == PatchOperationType.Rename ? 1 : 0,
                operation.NewFingerprint?.Length ?? 0),
            [operation]);

    private static PatchManifest EmptyManifest() =>
        new(
            "1.0",
            "patch-empty",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            "rules-v1",
            "sha256:base",
            "sha256:target",
            new PatchSummary(0, 0, 0, 0, 0),
            []);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
