namespace VaultDelta.Application.Abstractions;

public interface IApplyCapabilityValidator
{
    ValueTask ValidateAsync(
        string targetRoot,
        string transactionRoot,
        CancellationToken cancellationToken = default);
}
