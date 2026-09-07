using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace VaultDelta.Domain.Paths;

public sealed record RelativePath
{
    private static readonly SearchValues<char> WindowsInvalidCharacters =
        SearchValues.Create(['<', '>', ':', '"', '|', '?', '*']);

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9",
    };

    private RelativePath(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static IEqualityComparer<RelativePath> PortableComparer { get; } =
        new PortableRelativePathComparer();

    public static RelativePath Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value[0] is '/' or '\\')
        {
            throw new ArgumentException("A relative path cannot start with a directory separator.", nameof(value));
        }

        string normalized = value.Replace('\\', '/').Normalize(NormalizationForm.FormC);
        string[] segments = normalized.Split('/');

        foreach (string segment in segments)
        {
            ValidateSegment(segment, value);
        }

        return new RelativePath(string.Join('/', segments));
    }

    public override string ToString() => Value;

    private static void ValidateSegment(string segment, string originalValue)
    {
        if (segment.Length == 0 || segment is "." or "..")
        {
            throw new ArgumentException("A relative path must contain only non-empty file name segments.", nameof(originalValue));
        }

        if (segment.EndsWith(' ') || segment.EndsWith('.'))
        {
            throw new ArgumentException("Path segments cannot end with a space or period.", nameof(originalValue));
        }

        if (segment.AsSpan().IndexOfAny(WindowsInvalidCharacters) >= 0 || segment.Any(char.IsControl))
        {
            throw new ArgumentException("The path contains characters that are unsafe on a supported platform.", nameof(originalValue));
        }

        string deviceName = segment.Split('.', 2)[0];
        if (WindowsReservedNames.Contains(deviceName))
        {
            throw new ArgumentException("The path contains a Windows reserved device name.", nameof(originalValue));
        }
    }

    private sealed class PortableRelativePathComparer : IEqualityComparer<RelativePath>
    {
        public bool Equals(RelativePath? x, RelativePath? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            return x is not null
                && y is not null
                && StringComparer.OrdinalIgnoreCase.Equals(x.Value, y.Value);
        }

        public int GetHashCode(RelativePath obj) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Value);
    }
}
