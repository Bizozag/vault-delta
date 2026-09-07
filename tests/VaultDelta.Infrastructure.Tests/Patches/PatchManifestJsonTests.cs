using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;
using VaultDelta.Infrastructure.Patches;

namespace VaultDelta.Infrastructure.Tests.Patches;

public sealed class PatchManifestJsonTests
{
    [Fact]
    public void Serialize_is_deterministic_and_round_trips()
    {
        PatchManifest manifest = CreateManifest();

        byte[] first = PatchManifestJson.Serialize(manifest);
        byte[] second = PatchManifestJson.Serialize(manifest);
        PatchManifest restored = PatchManifestJson.Deserialize(first);

        Assert.Equal(first, second);
        Assert.Equal(manifest.PatchId, restored.PatchId);
        Assert.Equal(manifest.Operations, restored.Operations);
        Assert.EndsWith("\n", System.Text.Encoding.UTF8.GetString(first), StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_rejects_an_unknown_major_version()
    {
        byte[] json = PatchManifestJson.Serialize(CreateManifest());
        string changed = System.Text.Encoding.UTF8.GetString(json).Replace("\"1.0\"", "\"2.0\"", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => PatchManifestJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(changed)));
    }

    [Fact]
    public void Deserialize_rejects_an_unknown_operation_type()
    {
        byte[] json = PatchManifestJson.Serialize(CreateManifest());
        string changed = System.Text.Encoding.UTF8.GetString(json).Replace("\"add\"", "\"execute\"", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => PatchManifestJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(changed)));
    }

    [Fact]
    public void Deserialize_rejects_an_inconsistent_payload_path()
    {
        byte[] json = PatchManifestJson.Serialize(CreateManifest());
        string changed = System.Text.Encoding.UTF8.GetString(json).Replace(
            "files/Notes/new.md",
            "files/other.md",
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => PatchManifestJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(changed)));
    }

    private static PatchManifest CreateManifest()
    {
        FileFingerprint fingerprint = new(5, DateTimeOffset.UnixEpoch, new string('a', 64));
        PatchOperation operation = PatchOperation.Add(10, RelativePath.Parse("Notes/new.md"), fingerprint);
        return new PatchManifest(
            "1.0",
            "patch-001",
            new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.Zero),
            "0.1.0",
            "rules-v1",
            "sha256:base",
            "sha256:target",
            new PatchSummary(1, 0, 0, 0, 5),
            [operation]);
    }
}
