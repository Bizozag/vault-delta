namespace VaultDelta.Application.Apply;

public sealed record BaselineValidationProgress(int ProcessedOperations, int TotalOperations, string? CurrentPath);
