using System.Text;
using System.Text.RegularExpressions;

namespace VaultDelta.Domain.Rules;

public sealed class SnapshotRule
{
    private readonly Regex _matcher;

    public SnapshotRule(string id, SnapshotRuleAction action, string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown rule action.");
        }

        string normalizedPattern = pattern.Replace('\\', '/').Normalize(NormalizationForm.FormC);
        ValidatePattern(normalizedPattern);

        Id = id;
        Action = action;
        Pattern = normalizedPattern;
        _matcher = new Regex(
            ConvertGlobToRegex(normalizedPattern),
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking,
            TimeSpan.FromMilliseconds(100));
    }

    public string Id { get; }

    public SnapshotRuleAction Action { get; }

    public string Pattern { get; }

    public bool IsMatch(string relativePath) => _matcher.IsMatch(relativePath);

    private static void ValidatePattern(string pattern)
    {
        if (pattern[0] == '/' || pattern.Contains("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("Rule patterns must be canonical relative paths.", nameof(pattern));
        }

        string[] segments = pattern.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new ArgumentException("Rule patterns cannot contain empty, current, or parent segments.", nameof(pattern));
        }
    }

    private static string ConvertGlobToRegex(string pattern)
    {
        StringBuilder expression = new("^");

        for (int index = 0; index < pattern.Length; index++)
        {
            char current = pattern[index];
            if (current == '*')
            {
                bool isDoubleStar = index + 1 < pattern.Length && pattern[index + 1] == '*';
                if (isDoubleStar)
                {
                    index++;
                    bool followedBySeparator = index + 1 < pattern.Length && pattern[index + 1] == '/';
                    if (followedBySeparator)
                    {
                        index++;
                        expression.Append("(?:.*/)?");
                    }
                    else
                    {
                        expression.Append(".*");
                    }
                }
                else
                {
                    expression.Append("[^/]*");
                }

                continue;
            }

            if (current == '?')
            {
                expression.Append("[^/]");
                continue;
            }

            expression.Append(Regex.Escape(current.ToString()));
        }

        expression.Append('$');
        return expression.ToString();
    }
}
