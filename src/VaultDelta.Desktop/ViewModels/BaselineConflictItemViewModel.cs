using VaultDelta.Application.Apply;

namespace VaultDelta.Desktop.ViewModels;

public sealed record BaselineConflictItemViewModel(string Path, string TypeText, string Message)
{
    public static BaselineConflictItemViewModel Create(BaselineConflict conflict) =>
        new(
            conflict.Path.Value,
            conflict.Type switch
            {
                BaselineConflictType.MissingExpected => "缺少基线项",
                BaselineConflictType.UnexpectedContent => "内容不一致",
                BaselineConflictType.UnexpectedType => "类型不一致",
                BaselineConflictType.UnexpectedExisting => "目标已存在",
                BaselineConflictType.Unreadable => "无法读取",
                _ => conflict.Type.ToString(),
            },
            conflict.Message);
}
