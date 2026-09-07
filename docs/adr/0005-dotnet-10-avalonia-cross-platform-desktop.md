# ADR-0005：使用 .NET 10 与 Avalonia 交付跨平台桌面端

## Status

Accepted

## Context

Vault Delta 需要在 Windows 与 macOS 上执行相同的目录扫描、差异计算、补丁生成和事务应用。当前日期下 .NET 8 已接近支持结束，而 .NET 10 是支持周期更长的 LTS。macOS 需要同时覆盖 Apple Silicon 和仍在使用的 Intel 设备。MVP 没有必须调用完整 Apple 原生 API 的需求，也没有足够收益支撑 Rust/Web 前端或 Electron 重写。

## Decision

- 继续使用 C# 和模块化单体。
- 技术基线升级为 .NET 10 LTS 与 Avalonia。
- MVP 提供 Windows 和 macOS 桌面 UI，不提供公开 CLI。
- Application 与 Domain 保持 UI、平台无关，为未来 CLI 或其他壳层保留复用能力。
- 发布 `win-x64`、`osx-arm64` 和 `osx-x64` self-contained 产物。
- macOS 使用 Avalonia 原生后端提供的 Metal/OpenGL 渲染，不创建独立“Metal 版”。
- 对外 macOS 发布物封装为 `.app`/`.dmg`，执行 Developer ID 签名、hardened runtime 和公证。

## Consequences

### Positive

- 绝大多数领域与文件事务代码可跨平台复用。
- 单一语言、运行时和 UI 技术栈降低长期维护成本。
- Avalonia 支持 macOS ARM64/x64，并允许从 Windows 进行普通构建和交叉发布。
- 保留原生桌面拖放、文件选择与系统菜单体验。

### Negative

- macOS 签名、公证和最终安装验收需要 Mac、Xcode 命令行工具和 Apple Developer 证书。
- 文件系统大小写、Unicode 规范化、权限、符号链接和外接卷需要独立测试矩阵。
- 两种 macOS 架构增加发布产物与回归验证成本。

## Alternatives Considered

- 保持 .NET 8：支持周期过短，不适合作为 2026 年新项目基线。
- WPF：Windows 桌面成熟，但无法满足 macOS。
- Tauri/Rust：可生成较小应用，但引入 Rust 与 Web UI 两套生态，不能显著降低本项目最关键的文件安全验证成本。
- Electron：生态成熟，但运行体积和资源占用更高，对本地事务文件工具没有足够收益。
- .NET MAUI/Mac Catalyst：可行，但本项目更看重传统桌面文件操作和跨 Windows/macOS 的一致控件模型，Avalonia 更直接。

## Verification

- CI 必须构建 `win-x64`、`osx-arm64`、`osx-x64`。
- 至少在真实 Apple Silicon Mac 上执行 E2E、拖放和 Gatekeeper 验收。
- 发布前确认 manifest 的路径语义可在 Windows 与 macOS 间往返。

## References

- [Avalonia supported platforms](https://docs.avaloniaui.net/docs/supported-platforms)
- [Avalonia macOS platform guide](https://docs.avaloniaui.net/docs/platform-specific-guides/macos)
- [Avalonia macOS deployment](https://docs.avaloniaui.net/docs/deployment/macos)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
