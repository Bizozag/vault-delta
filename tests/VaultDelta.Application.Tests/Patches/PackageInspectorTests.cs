using VaultDelta.Application.Abstractions;
using VaultDelta.Application.Patches;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Tests.Patches;

public sealed class PackageInspectorTests
{
    [Fact]
    public async Task InspectAsync_accepts_an_exact_verified_package()
    {
        PatchManifest manifest = Manifest();
        FakeReader reader = new(
            manifest,
            [
                Entry("manifest.json", 10, 'b'),
                Entry("README.txt", 10, 'c'),
                Entry("files/note.md", 5, 'a'),
            ]);
        PackageInspector inspector = new(reader);

        PackageInspectionResult result = await inspector.InspectAsync("patch", CancellationToken.None);

        Assert.Equal(1, result.VerifiedPayloadCount);
        Assert.Equal(5, result.VerifiedPayloadBytes);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-hash")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("non-canonical")]
    [InlineData("wrong-summary")]
    public async Task InspectAsync_rejects_invalid_package_content(string scenario)
    {
        PatchManifest manifest = scenario == "wrong-summary" ? Manifest(payloadBytes: 6) : Manifest();
        List<PatchPackageEntry> entries =
        [
            Entry("manifest.json", 10, 'b'),
            Entry("README.txt", 10, 'c'),
        ];
        if (scenario != "missing")
        {
            entries.Add(Entry("files/note.md", 5, scenario == "wrong-hash" ? 'd' : 'a'));
        }

        if (scenario == "extra")
        {
            entries.Add(Entry("evil.exe", 1, 'e'));
        }

        if (scenario == "duplicate")
        {
            entries.Add(Entry("FILES/NOTE.MD", 5, 'a'));
        }

        if (scenario == "non-canonical")
        {
            entries.RemoveAll(entry => entry.Path == "files/note.md");
            entries.Add(Entry("files\\note.md", 5, 'a'));
        }

        PackageInspector inspector = new(new FakeReader(manifest, entries));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await inspector.InspectAsync("patch", CancellationToken.None));
    }

    private static PatchManifest Manifest(long payloadBytes = 5)
    {
        FileFingerprint fingerprint = new(5, DateTimeOffset.UnixEpoch, new string('a', 64));
        return new PatchManifest(
            "1.0",
            "patch-001",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            "rules-v1",
            "sha256:base",
            "sha256:target",
            new PatchSummary(1, 0, 0, 0, payloadBytes),
            [PatchOperation.Add(10, RelativePath.Parse("note.md"), fingerprint)]);
    }

    private static PatchPackageEntry Entry(string path, long length, char hash) =>
        new(path, length, new string(hash, 64));

    private sealed class FakeReader(PatchManifest manifest, IReadOnlyList<PatchPackageEntry> entries)
        : IPatchPackageReader
    {
        public ValueTask<PatchPackageContent> ReadAsync(
            string packagePath,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PatchPackageContent(manifest, entries));
    }
}
