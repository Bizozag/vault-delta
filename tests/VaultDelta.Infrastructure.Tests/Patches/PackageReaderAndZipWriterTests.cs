using System.IO.Compression;
using VaultDelta.Application.Patches;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;
using VaultDelta.Infrastructure.Hashing;
using VaultDelta.Infrastructure.Patches;

namespace VaultDelta.Infrastructure.Tests.Patches;

public sealed class PackageReaderAndZipWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vaultdelta-zip-{Guid.NewGuid():N}");

    public PackageReaderAndZipWriterTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Directory_reader_and_inspector_verify_a_directory_package()
    {
        (string source, PatchManifest manifest) = await CreateSourceAndManifestAsync("directory");
        string output = Path.Combine(_root, "directory-patch");
        Sha256ContentHasher hasher = new();
        DirectoryPackageWriter writer = new(hasher);
        await writer.WriteAsync(manifest, source, output, CancellationToken.None);
        PackageInspector inspector = new(new DirectoryPackageReader(hasher));

        PackageInspectionResult result = await inspector.InspectAsync(output, CancellationToken.None);

        Assert.Equal(1, result.VerifiedPayloadCount);
    }

    [Fact]
    public async Task Zip_writer_publishes_only_a_verified_zip()
    {
        (string source, PatchManifest manifest) = await CreateSourceAndManifestAsync("zip");
        string output = Path.Combine(_root, "patch.vaultdelta.zip");
        Sha256ContentHasher hasher = new();
        PackageInspector inspector = new(new ZipPackageReader());
        ZipPackageWriter writer = new(new DirectoryPackageWriter(hasher), inspector);

        await writer.WriteAsync(manifest, source, output, CancellationToken.None);
        PackageInspectionResult result = await inspector.InspectAsync(output, CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.Equal(1, result.VerifiedPayloadCount);
        Assert.Empty(Directory.GetFileSystemEntries(_root, "*.incomplete-*"));
    }

    [Fact]
    public async Task Zip_reader_rejects_zip_slip_entries()
    {
        string zipPath = Path.Combine(_root, "zip-slip.zip");
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("../outside.txt");
            await using Stream stream = entry.Open();
            await stream.WriteAsync(new byte[] { 1 }, CancellationToken.None);
        }

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new ZipPackageReader().ReadAsync(zipPath, CancellationToken.None));
    }

    [Fact]
    public async Task Inspector_rejects_duplicate_zip_paths()
    {
        (string source, PatchManifest manifest) = await CreateSourceAndManifestAsync("duplicate");
        string directory = Path.Combine(_root, "duplicate-directory");
        string zipPath = Path.Combine(_root, "duplicate.zip");
        Sha256ContentHasher hasher = new();
        await new DirectoryPackageWriter(hasher).WriteAsync(manifest, source, directory, CancellationToken.None);
        ZipFile.CreateFromDirectory(directory, zipPath);
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Update))
        {
            ZipArchiveEntry duplicate = archive.CreateEntry("FILES/NOTE.MD");
            await using Stream stream = duplicate.Open();
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("content"), CancellationToken.None);
        }

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new PackageInspector(new ZipPackageReader()).InspectAsync(zipPath, CancellationToken.None));
    }

    [Fact]
    public async Task Zip_reader_rejects_a_truncated_archive()
    {
        string zipPath = Path.Combine(_root, "truncated.zip");
        await File.WriteAllBytesAsync(zipPath, [0x50, 0x4B, 0x03, 0x04, 0x00], CancellationToken.None);

        await Assert.ThrowsAnyAsync<InvalidDataException>(async () =>
            await new ZipPackageReader().ReadAsync(zipPath, CancellationToken.None));
    }

    [Fact]
    public async Task Inspector_rejects_a_tampered_zip_payload()
    {
        (string source, PatchManifest manifest) = await CreateSourceAndManifestAsync("tampered");
        string directory = Path.Combine(_root, "tampered-directory");
        string zipPath = Path.Combine(_root, "tampered.zip");
        Sha256ContentHasher hasher = new();
        await new DirectoryPackageWriter(hasher).WriteAsync(manifest, source, directory, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(directory, "files", "note.md"), "tampered", CancellationToken.None);
        ZipFile.CreateFromDirectory(directory, zipPath);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new PackageInspector(new ZipPackageReader()).InspectAsync(zipPath, CancellationToken.None));
    }

    [Fact]
    public async Task Inspector_rejects_undeclared_zip_entries()
    {
        (string source, PatchManifest manifest) = await CreateSourceAndManifestAsync("extra");
        string directory = Path.Combine(_root, "extra-directory");
        string zipPath = Path.Combine(_root, "extra.zip");
        Sha256ContentHasher hasher = new();
        await new DirectoryPackageWriter(hasher).WriteAsync(manifest, source, directory, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(directory, "extra.exe"), "unexpected", CancellationToken.None);
        ZipFile.CreateFromDirectory(directory, zipPath);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new PackageInspector(new ZipPackageReader()).InspectAsync(zipPath, CancellationToken.None));
    }

    private async Task<(string Source, PatchManifest Manifest)> CreateSourceAndManifestAsync(string name)
    {
        string source = Path.Combine(_root, $"source-{name}");
        Directory.CreateDirectory(source);
        string filePath = Path.Combine(source, "note.md");
        await File.WriteAllTextAsync(filePath, "content", CancellationToken.None);
        FileInfo file = new(filePath);
        string hash = await new Sha256ContentHasher().ComputeSha256Async(filePath, CancellationToken.None);
        FileFingerprint fingerprint = new(file.Length, file.LastWriteTimeUtc, hash);
        PatchManifest manifest = new(
            "1.0",
            $"patch-{name}",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            "rules-v1",
            "sha256:base",
            "sha256:target",
            new PatchSummary(1, 0, 0, 0, file.Length),
            [PatchOperation.Add(10, RelativePath.Parse("note.md"), fingerprint)]);
        return (source, manifest);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
