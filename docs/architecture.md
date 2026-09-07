# Vault Delta 系统架构

## 1. 架构概览

Vault Delta 采用模块化单体架构。核心逻辑作为独立 .NET 类库，MVP 桌面 UI 只负责编排与展示。补丁生成器和补丁应用器使用同一套领域模型和清单验证规则，但运行在清晰分离的工作流中。Application 保持 UI 无关，以便未来按需增加 CLI，而不把 CLI 纳入首版范围。

```text
┌─────────────────────────────────────────────────────────────┐
│                       Desktop UI                            │
│  路径选择 · 进度 · 差异审核 · 风险确认 · 日志与结果展示      │
└──────────────────────────────┬──────────────────────────────┘
                               │ Application commands
┌──────────────────────────────▼──────────────────────────────┐
│                    Application Layer                        │
│ CompareWorkflow · BuildWorkflow · ApplyWorkflow · Rollback  │
└───────┬──────────────┬───────────────┬──────────────┬───────┘
        │              │               │              │
┌───────▼──────┐ ┌─────▼──────┐ ┌──────▼──────┐ ┌────▼───────┐
│ Scan/Hash    │ │ Diff Engine │ │ Package I/O │ │ Apply Txn  │
│ Inventory    │ │ Classifier  │ │ Manifest    │ │ Backup     │
└───────┬──────┘ └─────┬──────┘ └──────┬──────┘ └────┬───────┘
        └───────────────┴───────────────┴──────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                   Platform Adapters                         │
│ FileSystem · Zip · Clock · Hash · LongPath · Logging        │
└─────────────────────────────────────────────────────────────┘
```

## 2. 技术基线

- Runtime：.NET 10 LTS。
- 语言：C#，启用 nullable reference types。
- 桌面 UI：Avalonia。
- 序列化：System.Text.Json。
- 哈希：SHA-256；实现隐藏在 `IContentHasher` 后。
- 测试：xUnit、FluentAssertions，必要时使用 FsCheck 做属性测试。
- 日志：结构化 JSONL，同时生成面向用户的摘要文本。

## 3. 解决方案边界

计划中的解决方案结构：

```text
VaultDelta.sln
src/
  VaultDelta.Domain/
  VaultDelta.Application/
  VaultDelta.Infrastructure/
  VaultDelta.Desktop/
tests/
  VaultDelta.Domain.Tests/
  VaultDelta.Application.Tests/
  VaultDelta.Infrastructure.Tests/
  VaultDelta.EndToEnd.Tests/
docs/
```

依赖方向必须保持：

```text
Desktop → Application → Domain
Infrastructure → Application/Domain interfaces
Domain → 无项目依赖
```

Domain 不允许引用 UI、文件系统或压缩库。Application 不允许直接调用 `File.*`，必须通过端口接口访问外部世界。

## 4. 运行与发布矩阵

| 平台 | Runtime Identifier | MVP 产物 | 验收重点 |
|---|---|---|---|
| Windows x64 | `win-x64` | self-contained 应用目录/安装包 | 长路径、文件占用、NTFS 原子替换 |
| macOS Apple Silicon | `osx-arm64` | `.app`，公开发布时置于 `.dmg` | 真实 ARM64 运行、签名、公证、外接卷 |
| macOS Intel | `osx-x64` | `.app`，公开发布时置于 `.dmg` | x64 构建、启动与兼容性验证 |

Avalonia 的原生 macOS 后端提供窗口、输入、拖放、文件对话框、辅助功能以及 Metal/OpenGL 渲染。Metal 是渲染后端能力，不是需要单独维护的业务版本。核心项目使用普通 `net10.0`，只有确实需要 Apple 专属 API 时才评估 `net10.0-macos` 工作负载。

Windows 可完成 macOS 交叉编译，但 Developer ID 签名、hardened runtime、公证和最终安装验证必须在 macOS 环境或 macOS CI runner 上执行。

## 5. 数据模型

主要不可变模型：

- `RelativePath`：已规范化、已验证的相对路径值对象。
- `FileFingerprint`：长度、修改时间、SHA-256、可选文件标识。
- `SnapshotEntry`：相对路径、类型、指纹和属性。
- `SnapshotInventory`：快照元数据与有序条目集合。
- `DiffEntry`：新增、修改、删除、重命名或未变化。
- `PatchManifest`：补丁身份、基线、目标、操作和载荷摘要。
- `ApplyJournal`：应用事务的逐步状态和备份映射。

## 6. 核心不变量

1. 清单中的每条路径都是根目录下的规范相对路径。
2. 同一目标路径最多有一个最终写操作。
3. 删除和覆盖之前必须存在已验证的备份记录。
4. 所有载荷文件必须在复制前后通过清单哈希验证。
5. 生成成功的补丁包不得引用临时目录或源机器绝对路径。
6. Apply 成功意味着所有清单操作完成且最终指纹匹配。
7. Rollback 成功意味着所有已执行操作按逆序撤销并完成校验。

## 7. 平台适配边界

Infrastructure 提供统一接口并封装平台差异：

- 路径比较、大小写折叠和 Unicode 规范化检测。
- 符号链接、Windows 重解析点和 macOS 特殊条目的识别与拒绝。
- 同卷临时文件、原子 rename/replace、flush 与权限错误映射。
- Windows 长路径和 macOS 外接卷/大小写敏感卷行为。
- 应用包路径、日志与备份目录的系统约定。

为保证补丁可从一个平台传到另一个平台，manifest 使用 `/` 作为分隔符，并拒绝经大小写折叠后冲突的路径，即使生成端位于大小写敏感卷。

## 8. 扩展策略

MVP 不引入插件系统。未来扩展通过稳定接口完成：

- 新哈希算法。
- 新压缩容器。
- 新过滤预设。
- 新平台文件系统适配器。
- 块级差分生成器。

只有当第二种实现真实出现时才抽象可插拔注册机制，避免提前工程化。

## 9. 主要失败模式

| 失败 | 影响 | 处理 |
|---|---|---|
| 输入目录扫描中断 | 无差异结果 | 取消并丢弃内存态结果 |
| 文件扫描期间被修改 | 指纹不稳定 | 重新读取；仍变化则标为不稳定并阻断 |
| 输出空间不足 | 补丁不完整 | 写入临时目录并删除未完成产物 |
| 补丁被篡改或损坏 | 错误更新 | Apply 预检时哈希失败并阻断 |
| 目标端存在独立修改 | 可能覆盖用户数据 | 基线冲突列表，默认阻断 |
| 应用进程崩溃 | 目标处于中间态 | 下次启动读取 Journal，强制继续回滚或恢复 |
| 备份目录不可写 | 无法恢复 | 在任何修改前阻断 |
| 平台不支持的路径或特殊条目 | 补丁无法安全跨平台 | 生成阶段阻断并列出冲突路径 |
| macOS 应用未签名或公证失败 | 用户无法可信安装 | 发布流水线失败，不发布该候选版本 |

## 10. 可观测性

每次操作生成一个 `operationId`。日志至少记录：

- 阶段开始、完成、取消和失败。
- 输入根路径的脱敏标识与快照 ID。
- 文件数、字节数、哈希数和耗时。
- 每个 Apply 操作的序号、路径、动作与结果。
- 备份目录、Journal 路径和回滚状态。

日志不得记录文件内容。
