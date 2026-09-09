namespace VaultDelta.Domain.Rules;

public static class ObsidianDefaultRules
{
    public const string PresetName = "obsidian-default-v1";

    public static SnapshotRuleSet Create() =>
        SnapshotRuleSet.Create(PresetName, []);
}
