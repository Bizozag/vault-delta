using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Application.Abstractions;

public sealed record TargetEntryState
{
    private TargetEntryState(TargetEntryStateKind kind, FileFingerprint? fingerprint, string? error)
    {
        Kind = kind;
        Fingerprint = fingerprint;
        Error = error;
    }

    public TargetEntryStateKind Kind { get; }

    public FileFingerprint? Fingerprint { get; }

    public string? Error { get; }

    public static TargetEntryState Missing { get; } = new(TargetEntryStateKind.Missing, null, null);

    public static TargetEntryState Directory { get; } = new(TargetEntryStateKind.Directory, null, null);

    public static TargetEntryState File(FileFingerprint fingerprint) =>
        new(TargetEntryStateKind.File, fingerprint ?? throw new ArgumentNullException(nameof(fingerprint)), null);

    public static TargetEntryState Unreadable(string error) =>
        new(TargetEntryStateKind.Unreadable, null, string.IsNullOrWhiteSpace(error) ? "Unknown read error." : error);
}
