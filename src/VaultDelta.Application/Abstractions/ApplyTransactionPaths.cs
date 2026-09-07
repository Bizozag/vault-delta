namespace VaultDelta.Application.Abstractions;

public sealed record ApplyTransactionPaths(string OperationRoot, string BackupRoot, string JournalPath);
