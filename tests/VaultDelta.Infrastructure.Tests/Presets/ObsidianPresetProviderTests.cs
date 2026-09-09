using VaultDelta.Infrastructure.Presets;
using VaultDelta.Domain.Rules;

namespace VaultDelta.Infrastructure.Tests.Presets;

public sealed class ObsidianPresetProviderTests
{
    [Fact]
    public void LoadDefault_reads_the_embedded_versioned_preset()
    {
        var rules = ObsidianPresetProvider.LoadDefault();

        Assert.Equal("obsidian-default-v1", rules.Name);
        Assert.Empty(rules.Rules);
        Assert.True(rules.Evaluate(VaultDelta.Domain.Paths.RelativePath.Parse(".trash/deleted.md")).IsIncluded);
        Assert.True(rules.Evaluate(VaultDelta.Domain.Paths.RelativePath.Parse(".obsidian/plugins/test/main.js")).IsIncluded);
        Assert.Equal(ObsidianDefaultRules.Create().RulesId, rules.RulesId);
    }
}
