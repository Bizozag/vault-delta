namespace VaultDelta.Application.Compare;

public sealed record CompareSummary(
    int AddedCount,
    int ModifiedCount,
    int DeletedCount,
    int RenamedCount,
    int UnchangedCount,
    int TransferFileCount,
    long TransferBytes,
    int RiskCount)
{
    public int ChangedCount => AddedCount + ModifiedCount + DeletedCount + RenamedCount;
}
