using VaultDelta.Application.Compare;
using VaultDelta.Domain.Diffs;
using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Desktop.ViewModels;

public sealed class DiffReviewItemViewModel
{
    private DiffReviewItemViewModel(
        DiffEntry entry,
        bool isRisk,
        string path,
        string detail,
        string sizeText,
        string riskText)
    {
        Entry = entry;
        IsRisk = isRisk;
        Path = path;
        Detail = detail;
        SizeText = sizeText;
        RiskText = riskText;
    }

    public DiffEntry Entry { get; }
    public DiffEntryType Type => Entry.Type;
    public string TypeText => Type switch
    {
        DiffEntryType.Added => "新增",
        DiffEntryType.Modified => "修改",
        DiffEntryType.Deleted => "删除",
        DiffEntryType.Renamed => "重命名",
        _ => Type.ToString(),
    };
    public string Path { get; }
    public string Detail { get; }
    public string SizeText { get; }
    public bool IsRisk { get; }
    public string RiskText { get; }
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
    public bool HasRisk => !string.IsNullOrEmpty(RiskText);

    public static DiffReviewItemViewModel Create(DiffEntry entry, DiffSet differences)
    {
        bool isRisk = DifferenceRiskClassifier.IsRisk(entry, differences);
        string path = (entry.TargetPath ?? entry.BasePath)!.Value;
        string detail = entry.Type == DiffEntryType.Renamed
            ? $"{entry.BasePath!.Value}  →  {entry.TargetPath!.Value}"
            : string.Empty;
        SnapshotEntry? sizedEntry = entry.TargetEntry ?? entry.BaseEntry;
        string sizeText = sizedEntry?.Fingerprint is null
            ? "目录"
            : FormatBytes(sizedEntry.Fingerprint.Length);
        string riskText = entry.Type switch
        {
            DiffEntryType.Deleted => "目标端会移除此项",
            DiffEntryType.Renamed => "目标端会移动路径",
            _ when path.Equals(".obsidian", StringComparison.Ordinal)
                || path.StartsWith(".obsidian/", StringComparison.Ordinal) => "Obsidian 配置变更",
            _ when isRisk => "同路径类型发生变化",
            _ => string.Empty,
        };
        return new DiffReviewItemViewModel(entry, isRisk, path, detail, sizeText, riskText);
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
