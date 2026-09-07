# Vault Delta 模块设计

## 1. Presentation

### Desktop

职责：

- 选择目录和输出位置。
- 展示扫描进度、差异结果和风险。
- 允许筛选与取消选择。
- 启动补丁生成、应用和回滚。
- 在破坏性阶段前显示确认摘要。

边界：不读取文件内容、不计算差异、不直接复制或删除文件。

### Future CLI Adapter

MVP 不提供公开 CLI。Application 工作流不依赖 UI，后续只有在出现自动化需求且能够承担额外参数设计、错误码、文档和独立验收成本时，才增加轻量 CLI 适配器。

## 2. Snapshot Scanner

输入：根目录、过滤规则、链接策略、取消令牌。

输出：有序的 `SnapshotInventory` 和扫描诊断。

子组件：

- `DirectoryWalker`：递归枚举，不跟随链接或特殊文件系统条目。
- `PathNormalizer`：生成安全的 `RelativePath`。
- `FilterEvaluator`：应用包含/排除规则。
- `MetadataReader`：获取大小、时间戳和属性。
- `StabilityChecker`：读取前后比较元数据，检测扫描期间变化。

验证边界：扫描器只接受存在、可读且互不嵌套的根目录；任何路径逃逸、访问拒绝或不稳定文件进入诊断，严格模式下阻断。

## 3. Fingerprint Service

职责：

- 快速比较元数据。
- 对候选变化文件流式计算 SHA-256。
- 缓存本次工作流内的哈希结果。
- 计算快照摘要。

策略：

1. 路径和类型不同直接进入分类。
2. 大小不同则确定内容不同。
3. 大小相同且时间戳可信时可暂判相同。
4. 补丁生成和基线校验阶段对受影响文件强制哈希。

验证边界：哈希前后文件长度和最后修改时间必须保持一致，否则重新计算一次；第二次仍变化则阻断。

## 4. Diff Engine

输入：旧、新两个 inventory 及指纹解析器。

输出：稳定排序的 `DiffSet`。

分类：

- Added：只存在于新快照。
- Modified：路径相同但内容哈希不同。
- Deleted：只存在于旧快照。
- Renamed：旧路径删除与新路径新增，内容哈希相同。
- Unchanged：路径和内容相同。

重命名检测仅在哈希唯一匹配时成立。多个相同内容候选不得猜测，保留为 Added + Deleted。

验证边界：

- 同一相对路径经大小写折叠后冲突时在所有平台阻断，确保补丁可移植到 Windows 或大小写不敏感的 macOS 卷。
- 文件与目录同路径类型变化拆为删除旧类型、创建新类型，并标为高风险。
- DiffSet 必须满足目标路径唯一性。

## 5. Rule and Preset Engine

规则类型：

- Glob include/exclude。
- 路径前缀。
- 文件扩展名。
- 文件大小上限。
- 隐藏/系统属性。
- Obsidian 预设。

MVP 预设建议：

```text
默认排除：
  Thumbs.db
  Desktop.ini
  .DS_Store
  .trash/**
  .obsidian/workspace.json
  .obsidian/workspace-mobile.json

默认包含：
  .obsidian/plugins/**
  .obsidian/themes/**
```

验证边界：规则解析失败时不得静默忽略；界面必须展示实际生效规则和被排除数量。

## 6. Package Builder

输入：已审核 DiffSet、新快照、输出位置、包选项。

输出：默认 ZIP；可选完整补丁目录。

步骤：

1. 估算空间并创建临时 staging。
2. 复制 Added/Modified/Renamed 的新内容到 `files/`。
3. 每次复制后验证长度和哈希。
4. 生成规范化 manifest。
5. 生成 README 与兼容应用版本说明。
6. 对包内容生成总摘要。
7. 默认压缩为 ZIP 并重新读取验证；目录模式跳过压缩但执行同等 Inspector 校验。
8. 原子发布最终产物。

验证边界：任何一步失败则不发布最终包；临时产物带 `.incomplete` 标记并可安全清理。

## 7. Package Inspector

职责：

- 读取目录包或 ZIP。
- 检查 schema 版本和必填字段。
- 重新验证所有载荷哈希。
- 验证路径、安全约束和操作冲突。
- 输出人类可读摘要。

该模块必须在 Apply 之前无条件运行，不能由 UI 跳过。

## 8. Baseline Validator

输入：补丁 manifest 和目标库。

输出：`BaselineValidationResult`。

校验分层：

- 快速模式：验证所有将被修改、删除、重命名的旧路径。
- 严格模式：验证基线快照摘要涉及的全部受管理文件。

冲突分类：

- MissingExpected：目标缺少预期旧文件。
- UnexpectedContent：目标文件存在但哈希不符。
- UnexpectedType：预期文件但实际为目录，或相反。
- Locked/Unreadable：无法安全读取。
- ExtraUnmanaged：目标存在基线外文件；默认仅警告。

默认策略：前三类和 Locked/Unreadable 阻断；ExtraUnmanaged 不阻断。

## 9. Apply Transaction

职责：

- 获取目标库独占锁。
- 创建备份目录和 Journal。
- 按安全顺序应用操作。
- 每一步落盘记录。
- 完成后验证并提交。

安全顺序：

1. 创建所需目录。
2. 备份将被覆盖、删除或占用重命名目标的条目。
3. 将新文件写到目标同目录的临时文件。
4. 验证临时文件。
5. 原子替换最终文件。
6. 执行重命名。
7. 将删除项移入备份区。
8. 清理空目录。
9. 最终校验。
10. Journal 标记 Committed。

验证边界：任何写入前必须完成整个预检和备份可写性检查；运行中失败立即停止并进入 NeedsRollback。

## 10. Rollback Engine

读取 Journal，逆序执行已完成操作：

- 删除本次新增的文件。
- 恢复被替换的旧文件。
- 撤销重命名。
- 恢复被删除的文件和目录。
- 验证恢复后的基线指纹。

Rollback 本身也写日志并可幂等重试。

## 11. Reporting and Logging

输出：

- `compare-report.json`
- `manifest.json`
- `apply-journal.json`
- `operation.log.jsonl`
- `summary.txt`

所有报告使用 UTF-8，无 BOM；机器格式使用稳定字段名，展示文本可本地化。

## 12. Platform and Distribution Adapters

职责：

- 将统一文件系统端口映射到 Windows 与 macOS 行为。
- 检测卷的大小写、链接、权限、原子替换与可用空间能力。
- 产出 `win-x64`、`osx-arm64`、`osx-x64` self-contained 发布目录。
- 将 macOS 发布目录封装为 `.app`，生成 `Info.plist`，并在发布流水线执行签名、公证和 `.dmg` 包装。

验证边界：平台能力探测失败或无法证明原子替换/备份可用时，Apply 必须在任何写入前阻断。
