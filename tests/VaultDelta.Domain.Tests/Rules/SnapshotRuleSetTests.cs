using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Rules;

namespace VaultDelta.Domain.Tests.Rules;

public sealed class SnapshotRuleSetTests
{
    [Fact]
    public void Evaluate_includes_paths_when_no_rule_matches()
    {
        SnapshotRuleSet rules = SnapshotRuleSet.Create("custom-v1", []);

        RuleEvaluation evaluation = rules.Evaluate(RelativePath.Parse("Notes/today.md"));

        Assert.True(evaluation.IsIncluded);
        Assert.Null(evaluation.MatchedRule);
    }

    [Fact]
    public void Evaluate_uses_the_last_matching_rule()
    {
        SnapshotRuleSet rules = SnapshotRuleSet.Create(
            "custom-v1",
            [
                new SnapshotRule("exclude-temp", SnapshotRuleAction.Exclude, "**/*.tmp"),
                new SnapshotRule("include-important", SnapshotRuleAction.Include, "Important/*.tmp"),
            ]);

        Assert.False(rules.Evaluate(RelativePath.Parse("Cache/file.tmp")).IsIncluded);
        RuleEvaluation included = rules.Evaluate(RelativePath.Parse("Important/keep.tmp"));
        Assert.True(included.IsIncluded);
        Assert.Equal("include-important", included.MatchedRule!.Id);
    }

    [Theory]
    [InlineData(".trash")]
    [InlineData(".trash/deleted.md")]
    [InlineData(".obsidian/workspace.json")]
    [InlineData(".obsidian/workspace-mobile.json")]
    [InlineData("Thumbs.db")]
    [InlineData("Assets/Thumbs.db")]
    [InlineData(".DS_Store")]
    [InlineData("Notes/.DS_Store")]
    public void Obsidian_default_excludes_transient_paths(string path)
    {
        SnapshotRuleSet rules = ObsidianDefaultRules.Create();

        Assert.False(rules.Evaluate(RelativePath.Parse(path)).IsIncluded);
    }

    [Theory]
    [InlineData(".obsidian/plugins/dataview/main.js")]
    [InlineData(".obsidian/plugins/dataview/manifest.json")]
    [InlineData(".obsidian/themes/Minimal/theme.css")]
    [InlineData(".obsidian/app.json")]
    [InlineData("Notes/today.md")]
    public void Obsidian_default_includes_managed_content(string path)
    {
        SnapshotRuleSet rules = ObsidianDefaultRules.Create();

        Assert.True(rules.Evaluate(RelativePath.Parse(path)).IsIncluded);
    }

    [Fact]
    public void Rule_set_id_is_stable_and_changes_with_rule_order()
    {
        SnapshotRule first = new("exclude-temp", SnapshotRuleAction.Exclude, "**/*.tmp");
        SnapshotRule second = new("include-important", SnapshotRuleAction.Include, "Important/*.tmp");

        SnapshotRuleSet ordered = SnapshotRuleSet.Create("custom-v1", [first, second]);
        SnapshotRuleSet repeated = SnapshotRuleSet.Create("custom-v1", [first, second]);
        SnapshotRuleSet reversed = SnapshotRuleSet.Create("custom-v1", [second, first]);

        Assert.Equal(ordered.RulesId, repeated.RulesId);
        Assert.NotEqual(ordered.RulesId, reversed.RulesId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/absolute/**")]
    [InlineData("../outside/**")]
    [InlineData("folder//file")]
    public void Rule_rejects_invalid_patterns(string pattern)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new SnapshotRule("invalid", SnapshotRuleAction.Exclude, pattern));
    }
}
