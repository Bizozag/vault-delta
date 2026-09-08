# 大型库性能与路径兼容验证记录

日期：2026-09-08

对应实施计划：Task 22。

## 目标与边界

本阶段建立可重复的大型 Obsidian 库合成器、日常 CI 回归档、发布前 10 万文件测量脚本，以及 Windows/macOS 路径和卷能力验证。

本阶段不改变扫描器的安全语义：每个文件仍执行流式 SHA-256、扫描前后元数据稳定性检查、链接拒绝和规则过滤。性能数据用于建立基线和识别数量级退化，不构成面向用户的硬性 SLA。

## 合成库模型

`SyntheticVaultGenerator` 为 Baseline 和 Target 分别创建指定数量的 Markdown 文件，路径均匀分布在 100 个一级目录中。文件具有固定内容格式和固定 UTC 修改时间，确保重复测量的快照输入稳定。

默认变更比例为 1%，分别制造：

- Modified：原路径内容变化。
- Deleted：仅存在于 Baseline。
- Added：仅存在于 Target。
- 一个 `.obsidian/plugins/**` 修改，用于确认插件仍属于受管理范围。
- 一个 `.trash/**` 修改，用于确认回收站始终排除。

文件物化采用有限并行度，避免单线程创建小文件成为主要测量噪声。CompareWorkflow 计时只覆盖两个真实目录的枚举、哈希、稳定性复查、inventory 构造、Diff 分类和摘要统计。

## 两档验证策略

### 日常 CI 档

默认每个快照 1,000 个合成文件。该测试随完整解决方案运行，设置 2 分钟的宽松灾难性回退阈值。阈值只用于发现死循环、异常串行退化或极端 I/O 回退，不用于比较不同 CI runner 的细微性能。

本机 Release 观测：

```text
文件数/快照：1,000
每类稀疏变化：10
生成时间：约 244 ms
双快照扫描、哈希与比较：约 908 ms
Diff 条目：1,216
```

### 发布测量档

使用以下命令运行 10 万文件基线：

```powershell
.\scripts\measure-vault-performance.ps1 -FileCount 100000
```

本机 Windows Release 实测：

```text
文件数/快照：100,000
每类稀疏变化：1,000
生成时间：23,848 ms
双快照扫描、哈希与比较：339,489 ms（约 5 分 39.5 秒）
Diff 条目：101,206
```

发布档使用 15 分钟灾难性回退阈值。当前性能说明安全扫描在 10 万文件规模可完成，但逐文件顺序哈希是主要成本。若未来需要显著缩短时间，应单独设计受控并行哈希、可验证快照缓存或增量索引，不应绕过稳定性检查。

## 路径兼容矩阵

真实文件系统测试覆盖：

| 场景 | 验证边界 |
| --- | --- |
| Windows 长路径 | 创建完整路径超过传统 `MAX_PATH` 260 字符的文件，并由生产扫描器读取 |
| 大小写碰撞 | 当卷可以同时存储 `Note.md` 与 `note.md` 时，portable inventory 必须拒绝 |
| Unicode NFC/NFD | 当卷可以同时存储组合与分解形式时，NFC 规范化后碰撞必须拒绝 |
| `.trash/**` | 合成库中的变化不进入 DiffSet |
| 插件目录 | `.obsidian/plugins/**` 的变化必须进入 Modified 和传输统计 |

在大小写不敏感或规范化文件名的卷上，无法物化两个独立文件时，测试验证由卷本身阻止该碰撞；在可以物化的卷上，由 `SnapshotInventory` 的 portable comparer 阻止生成不可跨平台传输的快照。

## 外接卷验证

外接卷必须通过真实挂载路径执行，不能由普通临时目录可靠模拟。命令示例：

```powershell
.\scripts\measure-vault-performance.ps1 `
  -FileCount 1000 `
  -ExternalVolumePath 'E:\'
```

macOS 示例：

```powershell
./scripts/measure-vault-performance.ps1 `
  -FileCount 1000 `
  -ExternalVolumePath '/Volumes/VaultTransfer'
```

脚本会在该卷创建隔离探测目录，并验证：目标可写、事务目录可写、同卷目录移动、原子替换、目标根不是链接，以及卷的大小写和 Unicode 行为。探测目录在测试结束后删除。

当前开发机未提供外接卷路径，因此本次没有记录具体 USB/移动卷结果。该项保留为 Windows 10/11 和真实 Apple Silicon Mac 发布候选验收步骤，不伪造为已通过。

## CI 与后续边界

现有 GitHub Actions 同时在 `windows-latest` 和 `macos-latest` 运行完整测试，因此日常档、长路径测试和可物化的路径碰撞测试会持续跨平台执行。

Task 22 不引入性能优化、不放宽路径安全规则，也不修改补丁格式。Windows 发布包、macOS 双架构 `.app`、签名与公证分别属于 Task 23–25。
