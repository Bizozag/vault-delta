# Obsidian 固定 Fixture 与全链路验证记录

日期：2026-09-08

对应实施计划：Task 21。

## 目标

建立可以随仓库版本管理、在 Windows 与 macOS 上重复物化的 Obsidian 样例库，并使用真实文件系统验证完整离线更新链路：

```text
Baseline → 扫描和比较 → Manifest → ZIP → Inspector
         → 目标基线 Gate → Apply → 目标等于 Target
         → Rollback → 目标恢复 Baseline
```

Fixture 位于 `tests/VaultDelta.EndToEnd.Tests/Fixtures/ObsidianVault/`，分为 `Baseline` 和 `Target` 两个蓝图。测试运行时复制到独立临时目录，避免修改仓库资产。

## 内容矩阵

固定样例覆盖：

- Markdown：内容修改、内容不变、删除、新增和唯一内容重命名。
- Canvas：节点和连线发生变化的 `.canvas` JSON。
- Excalidraw：包含 frontmatter、文本元素和 Drawing JSON 的 `.excalidraw.md`。
- 附件：逐字节变化的真实 PNG。
- 插件：`.obsidian/plugins/sample/main.js` 和 `manifest.json` 修改。
- 主题：`.obsidian/themes/Local Theme/theme.css` 新增。
- Obsidian 设置：`.obsidian/app.json` 保持不变。
- 排除项：`.trash/Discarded.md` 和 `.obsidian/workspace.json` 故意发生变化，但不得进入快照或补丁。

二进制附件在仓库中保存为 `.base64` 蓝图，测试物化时解码并去掉后缀。这使附件字节可审查，也避免文本补丁工具直接写二进制文件。仓库 `.gitattributes` 固定文本为 LF，因此 Markdown、JSON、JavaScript 和 CSS 在 Windows/macOS checkout 后保持相同内容哈希。

## 全链路断言

`ObsidianVaultRoundTripTests` 使用生产实现完成以下验证：

1. `LocalFileSystem`、`Sha256ContentHasher` 和 `SnapshotScanner` 扫描两个真实目录。
2. `CompareWorkflow` 对各类资产产生预期 Added、Modified、Deleted 和 Renamed。
3. `.trash/**`、`.obsidian/workspace.json` 不进入 inventory 和 DiffSet。
4. `PatchManifest.FromDiff` 形成完整操作序列。
5. `DirectoryPackageWriter` 和 `ZipPackageWriter` 生成最终 ZIP。
6. `ZipPackageReader` 和 `PackageInspector` 重新检查包内容。
7. ZIP 包含插件、主题和 PNG 载荷，不包含 Trash 或 workspace 状态。
8. `ApplyWorkflow` 在基线副本上执行事务，应用后的受管理文件树逐字节等于 Target。
9. 排除目录在应用期间保持本地原值，不被目标快照中的变化覆盖。
10. `RollbackWorkflow` 使用 Journal 和备份恢复，受管理文件树逐字节等于原 Baseline。

## 跨平台黄金值

相同 fixture 连续扫描必须得到相同 snapshot ID、操作序列和 manifest JSON。以下值被测试锁定：

```text
Baseline snapshot:
sha256:b1f936db1d5afd3000704e84000159766999e523d76a0ca9e7adc81d8d67e114

Target snapshot:
sha256:5d67c9a4dc6941b90e86cfd891e20a84d84fb64ed6424475f80a61fa710c7a1b

Manifest JSON SHA-256:
25bba961a235fb5532529c4834266d85ada3bcc518ed5787bd56b3d566a585dd
```

CI 已配置 Windows 与 macOS runner。任一平台出现换行、Unicode、路径排序、哈希或序列化差异，黄金值测试都会失败。

## Task 21 边界

本阶段不做大规模性能基准、长路径压力、大小写敏感卷或外接卷矩阵；这些属于 Task 22。Fixture 规模保持较小，目标是提供可读、确定且覆盖真实 Obsidian 文件类型的发布前回归基线。
