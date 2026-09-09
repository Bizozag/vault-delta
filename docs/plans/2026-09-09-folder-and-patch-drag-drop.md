# 文件夹与 ZIP 补丁拖放实现计划

日期：2026-09-09
目标版本：0.1.1

## 目标

- 比较页的旧快照和新快照区域接受 Explorer/Finder 拖入的单个文件夹。
- 补丁页接受单个 `.zip` 补丁，并立即复用现有完整性检查。
- 补丁页的目标库区域接受单个文件夹，仍必须通过基线 Gate 后才能应用。
- 所有拖放入口保留原有选择器按钮，不改变核心比较、生成、应用和回滚协议。

## 模块与数据流

1. `MainWindow` 只负责读取 Avalonia `IDataTransfer`，拒绝多选、非本地 URI 和错误类型。
2. `CompareWorkspaceViewModel` 规范化并确认目录存在，再写入对应快照路径。
3. `PatchWorkspaceViewModel` 规范化补丁路径、限制 `.zip` 扩展名并调用现有 `InspectAsync`；目标目录只更新选择状态。
4. `PackageInspector`、`BaselineValidator` 和 `ApplyWorkflow` 保持原样，拖放不构成绕过安全 Gate 的新通道。

## 验证边界

- 输入边界：只接受一个本地对象；文件夹不能进入 ZIP 区，普通文件不能进入目录区。
- 状态边界：扫描、生成、检查、应用或恢复进行中时拒绝新的拖放。
- 路径边界：路径必须能规范化且在拖放时仍然存在；非法路径不覆盖当前选择。
- 补丁边界：扩展名不区分大小写，但即使是 `.zip` 也必须通过 schema、manifest、载荷、哈希和路径检查。
- 应用边界：拖入目标库后仍需显式执行基线检查，只有 Gate 通过才启用“应用补丁”。
- 平台边界：自动化测试验证路由、状态和拒绝规则；Windows Explorer 与 macOS Finder 的真实拖放分别属于发布验收 Gate。

## 发布步骤

1. Debug 构建和拖放定向测试。
2. Headless Avalonia 渲染，确认四个投放区启用且最小窗口不溢出。
3. 完整 Release 门禁。
4. 提交并推送 `main`。
5. 从干净提交构建 `VaultDelta-0.1.1-win-x64.zip`，核对版本、提交、冒烟、结构和 SHA-256。
6. 创建 GitHub `v0.1.1` Release，上传 ZIP 与 `SHA256SUMS.txt`，保留 `v0.1.0` 供回溯。
