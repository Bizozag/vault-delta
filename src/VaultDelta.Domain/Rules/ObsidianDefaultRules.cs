namespace VaultDelta.Domain.Rules;

public static class ObsidianDefaultRules
{
    public const string PresetName = "obsidian-default-v1";

    public static SnapshotRuleSet Create() =>
        SnapshotRuleSet.Create(
            PresetName,
            [
                new SnapshotRule("exclude-thumbs-db", SnapshotRuleAction.Exclude, "**/Thumbs.db"),
                new SnapshotRule("exclude-desktop-ini", SnapshotRuleAction.Exclude, "**/Desktop.ini"),
                new SnapshotRule("exclude-ds-store", SnapshotRuleAction.Exclude, "**/.DS_Store"),
                new SnapshotRule("exclude-trash-root", SnapshotRuleAction.Exclude, ".trash"),
                new SnapshotRule("exclude-trash", SnapshotRuleAction.Exclude, ".trash/**"),
                new SnapshotRule("exclude-workspace", SnapshotRuleAction.Exclude, ".obsidian/workspace.json"),
                new SnapshotRule("exclude-mobile-workspace", SnapshotRuleAction.Exclude, ".obsidian/workspace-mobile.json"),
                new SnapshotRule("include-plugins", SnapshotRuleAction.Include, ".obsidian/plugins/**"),
                new SnapshotRule("include-themes", SnapshotRuleAction.Include, ".obsidian/themes/**"),
            ]);
}
