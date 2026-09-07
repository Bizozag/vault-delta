using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Infrastructure.Patches;

public static class PatchManifestJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] Serialize(PatchManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(ToDocument(manifest), Options);
        byte[] withNewline = new byte[json.Length + 1];
        json.CopyTo(withNewline, 0);
        withNewline[^1] = (byte)'\n';
        return withNewline;
    }

    public static PatchManifest Deserialize(ReadOnlySpan<byte> json)
    {
        try
        {
            ManifestDocument document = JsonSerializer.Deserialize<ManifestDocument>(json, Options)
                ?? throw new InvalidDataException("Manifest JSON is empty.");

            if (!document.SchemaVersion.StartsWith("1.", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported manifest schema version: {document.SchemaVersion}.");
            }

            PatchOperation[] operations = document.Operations.Select(FromDocument).ToArray();
            return new PatchManifest(
                document.SchemaVersion,
                document.PatchId,
                document.CreatedAtUtc,
                document.GeneratorVersion,
                document.RulesId,
                document.BaseSnapshotId,
                document.TargetSnapshotId,
                document.Summary,
                operations);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Manifest JSON is invalid.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Manifest content is invalid.", exception);
        }
    }

    private static ManifestDocument ToDocument(PatchManifest manifest) =>
        new(
            manifest.SchemaVersion,
            manifest.PatchId,
            manifest.CreatedAtUtc,
            manifest.GeneratorVersion,
            manifest.RulesId,
            manifest.BaseSnapshotId,
            manifest.TargetSnapshotId,
            manifest.Summary,
            manifest.Operations.Select(ToDocument).ToArray());

    private static OperationDocument ToDocument(PatchOperation operation) =>
        new(
            operation.Sequence,
            operation.Type.ToString().ToLowerInvariant(),
            operation.EntryKind.ToString().ToLowerInvariant(),
            operation.BasePath?.Value,
            operation.TargetPath?.Value,
            operation.PayloadPath?.Value,
            operation.OldFingerprint,
            operation.NewFingerprint);

    private static PatchOperation FromDocument(OperationDocument document)
    {
        PatchOperationType type = document.Type switch
        {
            "add" => PatchOperationType.Add,
            "modify" => PatchOperationType.Modify,
            "delete" => PatchOperationType.Delete,
            "rename" => PatchOperationType.Rename,
            _ => throw new InvalidDataException($"Unsupported manifest operation type: {document.Type}."),
        };

        SnapshotEntryKind entryKind = document.EntryKind switch
        {
            "file" => SnapshotEntryKind.File,
            "directory" => SnapshotEntryKind.Directory,
            _ => throw new InvalidDataException($"Unsupported manifest entry kind: {document.EntryKind}."),
        };

        if (entryKind == SnapshotEntryKind.Directory)
        {
            PatchOperation directoryOperation = type switch
            {
                PatchOperationType.Add => PatchOperation.AddDirectory(
                    document.Sequence,
                    RelativePath.Parse(Require(document.TargetPath, "targetPath"))),
                PatchOperationType.Delete => PatchOperation.DeleteDirectory(
                    document.Sequence,
                    RelativePath.Parse(Require(document.BasePath, "basePath"))),
                PatchOperationType.Rename => PatchOperation.RenameDirectory(
                    document.Sequence,
                    RelativePath.Parse(Require(document.BasePath, "basePath")),
                    RelativePath.Parse(Require(document.TargetPath, "targetPath"))),
                PatchOperationType.Modify => throw new InvalidDataException("Directories cannot have modify operations."),
                _ => throw new InvalidDataException($"Unsupported manifest operation type: {document.Type}."),
            };

            ValidateDeclaredFields(document, directoryOperation);
            return directoryOperation;
        }

        PatchOperation operation = type switch
        {
            PatchOperationType.Add => PatchOperation.Add(
                document.Sequence,
                RelativePath.Parse(Require(document.TargetPath, "targetPath")),
                Require(document.NewFingerprint, "newFingerprint")),
            PatchOperationType.Modify => PatchOperation.Modify(
                document.Sequence,
                RelativePath.Parse(Require(document.TargetPath, "targetPath")),
                Require(document.OldFingerprint, "oldFingerprint"),
                Require(document.NewFingerprint, "newFingerprint")),
            PatchOperationType.Delete => PatchOperation.Delete(
                document.Sequence,
                RelativePath.Parse(Require(document.BasePath, "basePath")),
                Require(document.OldFingerprint, "oldFingerprint")),
            PatchOperationType.Rename => PatchOperation.Rename(
                document.Sequence,
                RelativePath.Parse(Require(document.BasePath, "basePath")),
                RelativePath.Parse(Require(document.TargetPath, "targetPath")),
                Require(document.NewFingerprint, "newFingerprint")),
            _ => throw new InvalidDataException($"Unsupported manifest operation type: {document.Type}."),
        };

        ValidateDeclaredFields(document, operation);
        return operation;
    }

    private static void ValidateDeclaredFields(OperationDocument document, PatchOperation operation)
    {
        if (!StringComparer.Ordinal.Equals(document.PayloadPath, operation.PayloadPath?.Value))
        {
            throw new InvalidDataException("Manifest operation payloadPath is inconsistent with its target path.");
        }

        if (document.OldFingerprint != operation.OldFingerprint
            || document.NewFingerprint != operation.NewFingerprint)
        {
            throw new InvalidDataException("Manifest operation fingerprints are inconsistent with its operation type.");
        }
    }

    private static T Require<T>(T? value, string name) where T : class =>
        value ?? throw new InvalidDataException($"Manifest operation is missing {name}.");

    private static string Require(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"Manifest operation is missing {name}.")
            : value;

    private static JsonSerializerOptions CreateOptions() =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

    private sealed record ManifestDocument(
        string SchemaVersion,
        string PatchId,
        DateTimeOffset CreatedAtUtc,
        string GeneratorVersion,
        string RulesId,
        string BaseSnapshotId,
        string TargetSnapshotId,
        PatchSummary Summary,
        IReadOnlyList<OperationDocument> Operations);

    private sealed record OperationDocument(
        int Sequence,
        string Type,
        string EntryKind,
        string? BasePath,
        string? TargetPath,
        string? PayloadPath,
        FileFingerprint? OldFingerprint,
        FileFingerprint? NewFingerprint);
}
