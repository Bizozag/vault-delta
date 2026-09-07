using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using VaultDelta.Domain.Paths;

namespace VaultDelta.Domain.Rules;

public sealed class SnapshotRuleSet
{
    private SnapshotRuleSet(string name, IReadOnlyList<SnapshotRule> rules, string rulesId)
    {
        Name = name;
        Rules = rules;
        RulesId = rulesId;
    }

    public string Name { get; }

    public IReadOnlyList<SnapshotRule> Rules { get; }

    public string RulesId { get; }

    public static SnapshotRuleSet Create(string name, IEnumerable<SnapshotRule> rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(rules);

        SnapshotRule[] materialized = rules.ToArray();
        if (materialized.Any(rule => rule is null))
        {
            throw new ArgumentException("Rule sets cannot contain null rules.", nameof(rules));
        }

        HashSet<string> identifiers = new(StringComparer.Ordinal);
        foreach (SnapshotRule rule in materialized)
        {
            if (!identifiers.Add(rule.Id))
            {
                throw new ArgumentException($"Rule identifier is duplicated: {rule.Id}.", nameof(rules));
            }
        }

        return new SnapshotRuleSet(
            name,
            new ReadOnlyCollection<SnapshotRule>(materialized),
            ComputeRulesId(name, materialized));
    }

    public RuleEvaluation Evaluate(RelativePath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        SnapshotRule? matchedRule = null;
        bool isIncluded = true;

        foreach (SnapshotRule rule in Rules)
        {
            if (!rule.IsMatch(path.Value))
            {
                continue;
            }

            matchedRule = rule;
            isIncluded = rule.Action == SnapshotRuleAction.Include;
        }

        return new RuleEvaluation(isIncluded, matchedRule);
    }

    private static string ComputeRulesId(string name, IReadOnlyList<SnapshotRule> rules)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, name);
        foreach (SnapshotRule rule in rules)
        {
            Append(hash, rule.Id);
            Append(hash, rule.Action.ToString());
            Append(hash, rule.Pattern);
        }

        return $"sha256:{Convert.ToHexStringLower(hash.GetHashAndReset())}";
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
