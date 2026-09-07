namespace VaultDelta.Application.Apply;

public enum BaselineConflictType
{
    MissingExpected = 1,
    UnexpectedContent = 2,
    UnexpectedType = 3,
    UnexpectedExisting = 4,
    Unreadable = 5,
}
