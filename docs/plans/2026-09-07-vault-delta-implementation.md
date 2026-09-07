# Vault Delta MVP Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 构建一个 Windows 优先的桌面与 CLI 工具，能够生成、检查、应用并回滚 Obsidian 文件库的离线增量补丁。

**Architecture:** 使用 .NET 8 模块化单体。Domain 持有路径、指纹、差异和补丁模型；Application 编排 Gate 工作流；Infrastructure 实现文件系统、哈希、ZIP 和 Journal；CLI 与 Avalonia Desktop 共享 Application 服务。

**Tech Stack:** C#、.NET 8、Avalonia、System.Text.Json、xUnit、FluentAssertions、FsCheck。

---

## Phase 0：项目基线

### Task 1：创建解决方案和项目边界

**Files:**

- Create: `VaultDelta.sln`
- Create: `Directory.Build.props`
- Create: `src/VaultDelta.Domain/VaultDelta.Domain.csproj`
- Create: `src/VaultDelta.Application/VaultDelta.Application.csproj`
- Create: `src/VaultDelta.Infrastructure/VaultDelta.Infrastructure.csproj`
- Create: `src/VaultDelta.Cli/VaultDelta.Cli.csproj`
- Create: `src/VaultDelta.Desktop/VaultDelta.Desktop.csproj`
- Create: matching projects under `tests/`

Steps:

1. 使用 `dotnet new` 创建解决方案和项目。
2. 添加项目引用并验证依赖方向。
3. 在 `Directory.Build.props` 启用 nullable、warnings as errors 和 deterministic build。
4. 运行 `dotnet build`，预期成功。
5. 提交：`chore: scaffold solution and module boundaries`。

### Task 2：建立 CI 本地等价命令

**Files:**

- Create: `scripts/verify.ps1`
- Create: `.github/workflows/ci.yml`

Steps:

1. 脚本依次执行 format check、build、test。
2. CI 使用 Windows runner 和 .NET 8。
3. 本地运行脚本并确认成功。
4. 提交：`ci: add deterministic verification pipeline`。

## Phase 1：安全路径与快照模型

### Task 3：实现 RelativePath 值对象

**Files:**

- Create: `src/VaultDelta.Domain/Paths/RelativePath.cs`
- Create: `tests/VaultDelta.Domain.Tests/Paths/RelativePathTests.cs`

Steps:

1. 写合法 Unicode、路径分隔符规范化测试。
2. 写绝对路径、UNC、盘符、空段和 `..` 失败测试。
3. 运行测试，预期因类型不存在而失败。
4. 实现最小解析逻辑和 Windows 大小写等价比较器。
5. 运行定向和完整测试，预期通过。
6. 提交：`feat: add safe relative path model`。

### Task 4：实现快照与指纹模型

**Files:**

- Create: `src/VaultDelta.Domain/Snapshots/FileFingerprint.cs`
- Create: `src/VaultDelta.Domain/Snapshots/SnapshotEntry.cs`
- Create: `src/VaultDelta.Domain/Snapshots/SnapshotInventory.cs`
- Test: `tests/VaultDelta.Domain.Tests/Snapshots/`

Steps:

1. 写条目排序、路径唯一和大小写碰撞测试。
2. 写 snapshotId 输入确定性测试。
3. 实现不可变模型和构造验证。
4. 运行测试并提交：`feat: define snapshot inventory model`。

### Task 5：实现文件系统端口与目录扫描器

**Files:**

- Create: `src/VaultDelta.Application/Abstractions/IFileSystem.cs`
- Create: `src/VaultDelta.Application/Snapshots/SnapshotScanner.cs`
- Create: `src/VaultDelta.Infrastructure/FileSystem/LocalFileSystem.cs`
- Test: scanner unit and integration tests

Steps:

1. 用 fake filesystem 写枚举、过滤、取消测试。
2. 用真实临时目录写重解析点拒绝和不可读文件测试。
3. 实现端口、适配器和扫描编排。
4. 增加扫描前后元数据稳定性检查。
5. 运行测试并提交：`feat: scan stable snapshot inventories`。

## Phase 2：哈希、规则与差异

### Task 6：实现流式 SHA-256

**Files:**

- Create: `src/VaultDelta.Application/Abstractions/IContentHasher.cs`
- Create: `src/VaultDelta.Infrastructure/Hashing/Sha256ContentHasher.cs`
- Test: hashing unit and integration tests

测试空文件、大文件、取消和文件变化检测；实现固定缓冲流式读取。提交：`feat: add stable streaming fingerprints`。

### Task 7：实现规则与 Obsidian 预设

**Files:**

- Create: `src/VaultDelta.Domain/Rules/`
- Create: `src/VaultDelta.Infrastructure/Presets/obsidian-default-v1.json`
- Test: `tests/VaultDelta.Domain.Tests/Rules/`

测试 include/exclude 优先级、规则解析失败和实际生效统计。提交：`feat: add snapshot filtering rules`。

### Task 8：实现 Diff Engine

**Files:**

- Create: `src/VaultDelta.Domain/Diffs/`
- Test: `tests/VaultDelta.Domain.Tests/Diffs/`

Steps:

1. 分别写 Added、Modified、Deleted 测试。
2. 写唯一哈希重命名和重复内容歧义测试。
3. 写类型变化和大小写冲突测试。
4. 实现确定性分类和操作排序。
5. 加入性质测试：分类互斥、目标路径唯一、重复运行相同。
6. 提交：`feat: classify deterministic snapshot differences`。

### Phase Gate P2

- 通过路径安全矩阵。
- 对固定 fixture 生成稳定 DiffSet。
- 证明扫描和比较不修改输入目录。
- 完成 10 万文件性能基线记录。

## Phase 3：补丁包

### Task 9：实现 Manifest 模型和 JSON Schema

**Files:**

- Create: `src/VaultDelta.Domain/Patches/`
- Create: `docs/specs/manifest-v1.schema.json`
- Test: serialization golden files

验证稳定序列化、未知 major 拒绝和未知 operation 拒绝。提交：`feat: define patch manifest v1`。

### Task 10：实现 Package Builder

**Files:**

- Create: `src/VaultDelta.Application/Patches/PackageBuilder.cs`
- Create: `src/VaultDelta.Infrastructure/Patches/DirectoryPackageWriter.cs`
- Test: builder integration tests

使用 staging、逐文件校验和原子发布。测试空间不足、源文件变化和输出已存在。提交：`feat: build verified patch directories`。

### Task 11：实现 ZIP 封装与 Package Inspector

**Files:**

- Create: `src/VaultDelta.Infrastructure/Patches/ZipPackageWriter.cs`
- Create: `src/VaultDelta.Application/Patches/PackageInspector.cs`
- Test: corrupted package matrix

测试 ZIP 截断、重复路径、Zip Slip、清单外文件和载荷损坏。提交：`feat: inspect and archive patch packages`。

### Phase Gate P3

- 同一输入生成相同 manifest operations。
- Inspector 可发现所有故意损坏样例。
- 失败构建不留下可误用的最终包。

## Phase 4：事务应用和回滚

### Task 12：实现 Baseline Validator

**Files:**

- Create: `src/VaultDelta.Application/Apply/BaselineValidator.cs`
- Test: baseline validation matrix

测试 Pass、Warning、各类 Conflict 和严格模式。提交：`feat: validate target patch baselines`。

### Task 13：实现 Apply Journal 与恢复锁

**Files:**

- Create: `src/VaultDelta.Domain/Apply/`
- Create: `src/VaultDelta.Infrastructure/Apply/JsonApplyJournalStore.cs`
- Test: journal persistence and stale lock tests

测试每次状态转换可重新加载，陈旧锁和未完成事务可识别。提交：`feat: persist apply transaction state`。

### Task 14：实现备份与原子写

**Files:**

- Create: `src/VaultDelta.Infrastructure/Apply/BackupStore.cs`
- Create: `src/VaultDelta.Infrastructure/FileSystem/AtomicFileWriter.cs`
- Test: atomic write integration tests

测试覆盖前备份、同目录临时文件、flush、hash 和 replace。提交：`feat: safely stage target file changes`。

### Task 15：实现 Apply Transaction

**Files:**

- Create: `src/VaultDelta.Application/Apply/ApplyWorkflow.cs`
- Test: per-operation and failure injection tests

按操作序列实现 Add/Modify/Delete/Rename。为每个动作前后加入故障注入点。提交：`feat: apply patches transactionally`。

### Task 16：实现 Rollback Engine

**Files:**

- Create: `src/VaultDelta.Application/Apply/RollbackWorkflow.cs`
- Test: rollback integration tests

测试所有操作逆转、幂等重试和回滚中断恢复。提交：`feat: rollback interrupted patch applications`。

### Phase Gate P4

- 性质测试 `Apply(Build(A,B),A)==B` 通过。
- 性质测试 Apply 后 Rollback 恢复 A。
- 每个故障注入点均不造成不可恢复的数据丢失。

## Phase 5：CLI

### Task 17：实现 compare/build/inspect

**Files:**

- Create: `src/VaultDelta.Cli/Commands/`
- Test: CLI invocation tests

添加参数校验、JSON 输出和退出码。提交：`feat: add patch generation cli`。

### Task 18：实现 apply/rollback

默认交互确认；`--yes` 只跳过确认，不跳过任何 Gate。提交：`feat: add safe patch application cli`。

## Phase 6：Desktop UI

### Task 19：应用壳与导航

实现比较、补丁历史/检查、设置三个页面，与已确认预览保持一致。提交：`feat: add desktop application shell`。

### Task 20：比较和审核页面

实现目录选择、进度、取消、筛选、风险项与传输估算。提交：`feat: add interactive diff review`。

### Task 21：生成、应用与恢复体验

实现 Gate 状态、冲突列表、备份信息、失败恢复和结果摘要。提交：`feat: expose transactional patch workflows`。

## Phase 7：发布验证

### Task 22：固定 fixture 和 E2E 套件

覆盖 Markdown、Canvas、Excalidraw、附件和 `.obsidian`。提交：`test: add obsidian vault end-to-end fixtures`。

### Task 23：性能与长路径验证

新增合成数据生成器和性能基线报告。提交：`test: establish large vault performance baseline`。

### Task 24：打包和用户文档

创建 Windows x64 self-contained 包、快速入门、恢复指南和校验和。提交：`docs: prepare mvp release package`。

## MVP 完成定义

- Phase Gate P2、P3、P4 全部通过。
- CLI 与 Desktop 使用同一核心工作流。
- Windows 10/11 上完成真实副本库的生成、传输、应用和回滚演练。
- 未解决的高危或数据丢失缺陷为零。
- manifest v1、用户指南和恢复指南与实现一致。
