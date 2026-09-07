namespace VaultDelta.Application.Abstractions;

public sealed record PatchPackageEntry(string Path, long Length, string Sha256);
