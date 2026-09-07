using VaultDelta.Application.Abstractions;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;

namespace VaultDelta.Application.Patches;

public sealed class PackageInspector(IPatchPackageReader packageReader)
{
    private readonly IPatchPackageReader _packageReader =
        packageReader ?? throw new ArgumentNullException(nameof(packageReader));

    public async ValueTask<PackageInspectionResult> InspectAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        PatchPackageContent content = await _packageReader
            .ReadAsync(packagePath, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<RelativePath, PatchPackageEntry> actualEntries = new(RelativePath.PortableComparer);
        foreach (PatchPackageEntry entry in content.Entries)
        {
            RelativePath path = RelativePath.Parse(entry.Path);
            if (!StringComparer.Ordinal.Equals(path.Value, entry.Path))
            {
                throw new InvalidDataException($"Package entry path is not canonical: {entry.Path}.");
            }

            if (!actualEntries.TryAdd(path, entry))
            {
                throw new InvalidDataException($"Package contains a duplicate path: {entry.Path}.");
            }
        }

        HashSet<RelativePath> expectedEntries = new(RelativePath.PortableComparer)
        {
            RelativePath.Parse("manifest.json"),
            RelativePath.Parse("README.txt"),
        };
        int verifiedPayloadCount = 0;
        long verifiedPayloadBytes = 0;

        foreach (PatchOperation operation in content.Manifest.Operations.Where(item => item.PayloadPath is not null))
        {
            RelativePath payloadPath = operation.PayloadPath!;
            if (!expectedEntries.Add(payloadPath))
            {
                throw new InvalidDataException($"Multiple operations declare payload {payloadPath}.");
            }

            if (!actualEntries.TryGetValue(payloadPath, out PatchPackageEntry? actual))
            {
                throw new InvalidDataException($"Package is missing payload {payloadPath}.");
            }

            if (actual.Length != operation.NewFingerprint!.Length
                || !StringComparer.Ordinal.Equals(actual.Sha256, operation.NewFingerprint.Sha256))
            {
                throw new InvalidDataException($"Payload verification failed: {payloadPath}.");
            }

            verifiedPayloadCount++;
            verifiedPayloadBytes += actual.Length;
        }

        foreach (RelativePath expected in expectedEntries)
        {
            if (!actualEntries.ContainsKey(expected))
            {
                throw new InvalidDataException($"Package is missing required entry {expected}.");
            }
        }

        RelativePath[] undeclared = actualEntries.Keys
            .Where(path => !expectedEntries.Contains(path))
            .ToArray();
        if (undeclared.Length > 0)
        {
            throw new InvalidDataException($"Package contains undeclared entry {undeclared[0]}.");
        }

        ValidateSummary(content.Manifest, verifiedPayloadBytes);
        return new PackageInspectionResult(content.Manifest, verifiedPayloadCount, verifiedPayloadBytes);
    }

    private static void ValidateSummary(PatchManifest manifest, long verifiedPayloadBytes)
    {
        PatchSummary calculated = new(
            manifest.Operations.Count(operation => operation.Type == PatchOperationType.Add),
            manifest.Operations.Count(operation => operation.Type == PatchOperationType.Modify),
            manifest.Operations.Count(operation => operation.Type == PatchOperationType.Delete),
            manifest.Operations.Count(operation => operation.Type == PatchOperationType.Rename),
            verifiedPayloadBytes);

        if (calculated != manifest.Summary)
        {
            throw new InvalidDataException("Manifest summary does not match its operations and payloads.");
        }
    }
}
