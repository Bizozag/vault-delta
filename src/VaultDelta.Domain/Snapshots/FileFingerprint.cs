using System.Globalization;

namespace VaultDelta.Domain.Snapshots;

public sealed record FileFingerprint
{
    public FileFingerprint(long length, DateTimeOffset lastWriteTimeUtc, string sha256)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "File length cannot be negative.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal characters.", nameof(sha256));
        }

        Length = length;
        LastWriteTimeUtc = lastWriteTimeUtc.ToUniversalTime();
        Sha256 = sha256.ToLower(CultureInfo.InvariantCulture);
    }

    public long Length { get; }

    public DateTimeOffset LastWriteTimeUtc { get; }

    public string Sha256 { get; }
}
