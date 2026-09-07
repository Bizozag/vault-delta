using System.Text.Json;
using System.Text.Json.Serialization;
using VaultDelta.Domain.Rules;

namespace VaultDelta.Infrastructure.Presets;

public static partial class ObsidianPresetProvider
{
    private const string ResourceName = "VaultDelta.Infrastructure.Presets.obsidian-default-v1.json";

    public static SnapshotRuleSet LoadDefault()
    {
        using Stream stream = typeof(ObsidianPresetProvider).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded preset was not found: {ResourceName}.");
        PresetDocument document = JsonSerializer.Deserialize(stream, PresetJsonContext.Default.PresetDocument)
            ?? throw new InvalidDataException("The embedded Obsidian preset is empty.");

        return SnapshotRuleSet.Create(
            document.Name,
            document.Rules.Select(rule =>
                new SnapshotRule(rule.Id, ParseAction(rule.Action), rule.Pattern)));
    }

    private static SnapshotRuleAction ParseAction(string action) =>
        action.ToLowerInvariant() switch
        {
            "include" => SnapshotRuleAction.Include,
            "exclude" => SnapshotRuleAction.Exclude,
            _ => throw new InvalidDataException($"Unknown preset rule action: {action}."),
        };

    internal sealed record PresetDocument(string Name, IReadOnlyList<PresetRuleDocument> Rules);

    internal sealed record PresetRuleDocument(string Id, string Action, string Pattern);

    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(PresetDocument))]
    internal sealed partial class PresetJsonContext : JsonSerializerContext;
}
