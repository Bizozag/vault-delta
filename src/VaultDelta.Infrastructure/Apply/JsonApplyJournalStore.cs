using System.Text.Json;
using System.Text.Json.Serialization;
using VaultDelta.Application.Abstractions;
using VaultDelta.Domain.Apply;
using VaultDelta.Domain.Patches;
using VaultDelta.Domain.Paths;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Infrastructure.Apply;

public sealed class JsonApplyJournalStore : IApplyJournalStore
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async ValueTask SaveAsync(
        string journalPath,
        ApplyJournal journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        ArgumentNullException.ThrowIfNull(journal);

        string path = Path.GetFullPath(journalPath);
        string? parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent))
        {
            throw new ArgumentException("Journal path must have a parent directory.", nameof(journalPath));
        }

        Directory.CreateDirectory(parent);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(ToDocument(journal), _options);
        const int maxAttempts = 8;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (FileStream stream = new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporaryPath, path, overwrite: true);
                return;
            }
            catch (Exception exception) when (IsTransientAccessError(exception) && attempt < maxAttempts)
            {
                TryDeleteTemporary(temporaryPath);
                await Task.Delay(200 * attempt, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                TryDeleteTemporary(temporaryPath);
                throw;
            }
        }
    }

    private static bool IsTransientAccessError(Exception exception) =>
        (exception is IOException or UnauthorizedAccessException)
        && (exception.HResult & 0xFFFF) is 5 or 32 or 33;

    private static void TryDeleteTemporary(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Preserve the original journal write error if a sync client also holds the temporary file.
        }
    }

    public async ValueTask<ApplyJournal> LoadAsync(
        string journalPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        try
        {
            byte[] json = await File.ReadAllBytesAsync(journalPath, cancellationToken).ConfigureAwait(false);
            JournalDocument document = JsonSerializer.Deserialize<JournalDocument>(json, _options)
                ?? throw new InvalidDataException("Apply journal is empty.");
            if (!document.SchemaVersion.StartsWith("1.", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported apply journal version: {document.SchemaVersion}.");
            }

            return new ApplyJournal(
                document.SchemaVersion,
                document.OperationId,
                document.PatchId,
                document.TargetRoot,
                document.BackupRoot,
                document.CreatedAtUtc,
                document.UpdatedAtUtc,
                document.Status,
                document.Operations.Select(FromDocument).ToArray());
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Apply journal JSON is invalid.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Apply journal content is invalid.", exception);
        }
    }

    private static JournalDocument ToDocument(ApplyJournal journal) =>
        new(
            journal.SchemaVersion,
            journal.OperationId,
            journal.PatchId,
            journal.TargetRoot,
            journal.BackupRoot,
            journal.CreatedAtUtc,
            journal.UpdatedAtUtc,
            journal.Status,
            journal.Operations.Select(ToDocument).ToArray());

    private static JournalOperationDocument ToDocument(ApplyJournalOperation operation) =>
        new(
            new OperationDocument(
                operation.Operation.Sequence,
                operation.Operation.Type,
                operation.Operation.EntryKind,
                operation.Operation.BasePath?.Value,
                operation.Operation.TargetPath?.Value,
                operation.Operation.PayloadPath?.Value,
                operation.Operation.OldFingerprint,
                operation.Operation.NewFingerprint),
            operation.Status,
            operation.BackupRelativePath);

    private static ApplyJournalOperation FromDocument(JournalOperationDocument document)
    {
        OperationDocument operation = document.Operation;
        return new ApplyJournalOperation(
            PatchOperation.Restore(
                operation.Sequence,
                operation.Type,
                operation.EntryKind,
                Parse(operation.BasePath),
                Parse(operation.TargetPath),
                Parse(operation.PayloadPath),
                operation.OldFingerprint,
                operation.NewFingerprint),
            document.Status,
            document.BackupRelativePath);
    }

    private static RelativePath? Parse(string? value) =>
        value is null ? null : RelativePath.Parse(value);

    private sealed record JournalDocument(
        string SchemaVersion,
        string OperationId,
        string PatchId,
        string TargetRoot,
        string BackupRoot,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        ApplyJournalStatus Status,
        IReadOnlyList<JournalOperationDocument> Operations);

    private sealed record JournalOperationDocument(
        OperationDocument Operation,
        ApplyOperationStatus Status,
        string? BackupRelativePath);

    private sealed record OperationDocument(
        int Sequence,
        PatchOperationType Type,
        SnapshotEntryKind EntryKind,
        string? BasePath,
        string? TargetPath,
        string? PayloadPath,
        FileFingerprint? OldFingerprint,
        FileFingerprint? NewFingerprint);
}
