namespace VaultDelta.Application.Patches;

public enum PackageBuildStage
{
    Preparing = 1,
    CopyingPayloads = 2,
    WritingMetadata = 3,
    Compressing = 4,
    Verifying = 5,
    Publishing = 6,
    Completed = 7,
}
