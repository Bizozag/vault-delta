using System.Collections.ObjectModel;

namespace VaultDelta.Application.Apply;

public sealed class BaselineValidationResult
{
    public BaselineValidationResult(IEnumerable<BaselineConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        Conflicts = new ReadOnlyCollection<BaselineConflict>(conflicts.ToArray());
        Status = Conflicts.Count == 0
            ? BaselineValidationStatus.Pass
            : BaselineValidationStatus.Conflict;
    }

    public BaselineValidationStatus Status { get; }

    public IReadOnlyList<BaselineConflict> Conflicts { get; }
}
