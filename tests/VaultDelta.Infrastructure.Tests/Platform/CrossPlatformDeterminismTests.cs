using System.Security.Cryptography;
using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;
using VaultDelta.Infrastructure.Patches;

namespace VaultDelta.Infrastructure.Tests.Platform;

public sealed class CrossPlatformDeterminismTests
{
    private const string ExpectedManifestSha256 = "59ca51ee659e17b7ab2e3d2d7b306ab482fbf405c27d07c61a1f97e173bcf54e";

    [Fact]
    public void Fixed_unicode_obsidian_fixture_has_a_platform_independent_manifest()
    {
        SnapshotInventory baseline = SnapshotInventory.Create(
            "obsidian-default-v1",
            [
                File(".obsidian/plugins/demo/main.js", 'a', 10),
                File("笔记/旧名称.md", 'b', 20),
                File("Canvas/流程.canvas", 'c', 30),
                File("附件/图像.png", 'd', 40),
            ]);
        SnapshotInventory target = SnapshotInventory.Create(
            "obsidian-default-v1",
            [
                File(".obsidian/plugins/demo/main.js", 'e', 11),
                File("笔记/新名称.md", 'b', 20),
                File("Canvas/流程.canvas", 'c', 30),
                File("附件/图像.png", 'd', 40),
                File("笔记/école.md", 'f', 50),
            ]);
        PatchManifest manifest = PatchManifest.FromDiff(
            "cross-platform-fixture",
            new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero),
            "0.1.0",
            baseline,
            target,
            DiffEngine.Compare(baseline, target));

        string actual = Convert.ToHexStringLower(SHA256.HashData(PatchManifestJson.Serialize(manifest)));

        Assert.Equal(ExpectedManifestSha256, actual);
        Assert.Equal(
            [
                "modify:.obsidian/plugins/demo/main.js",
                "add:笔记/école.md",
                "rename:笔记/新名称.md",
            ],
            manifest.Operations.Select(operation =>
                $"{operation.Type.ToString().ToLowerInvariant()}:{operation.TargetPath?.Value ?? operation.BasePath!.Value}"));
    }

    private static SnapshotEntry File(string path, char hashCharacter, long length) =>
        new(
            RelativePath.Parse(path),
            SnapshotEntryKind.File,
            new FileFingerprint(length, DateTimeOffset.UnixEpoch, new string(hashCharacter, 64)));
}
