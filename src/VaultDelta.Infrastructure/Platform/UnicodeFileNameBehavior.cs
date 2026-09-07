namespace VaultDelta.Infrastructure.Platform;

public enum UnicodeFileNameBehavior
{
    PreservesDistinctForms = 1,
    TreatsCanonicalFormsAsEquivalent = 2,
    NormalizesStoredNames = 3,
}
