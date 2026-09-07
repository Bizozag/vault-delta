# ADR-0001：使用 .NET 8 模块化单体

## Status

Superseded by [ADR-0005](0005-dotnet-10-avalonia-cross-platform-desktop.md)

## Context

项目由小团队启动，主要运行在 Windows，需要可靠文件系统能力、桌面 UI、可移植 CLI 和低部署复杂度。核心比较和应用逻辑必须可独立测试，并为未来跨平台保留空间。

## Decision

采用 .NET 8 LTS 和 C#。使用模块化单体解决方案，Domain/Application/Infrastructure/Desktop/CLI 分项目，桌面 UI 使用 Avalonia。

## Consequences

### Positive

- 单一部署和调试模型。
- 强类型、良好异步与文件流支持。
- 核心库可被 UI 和 CLI 复用。
- Avalonia 保留 macOS/Linux 扩展可能。

### Negative

- Avalonia 的平台细节仍需额外验证。
- 首版发布体积大于原生 Win32 小工具。

## Alternatives Considered

- WPF：Windows 集成成熟，但完全锁定 Windows。
- Tauri/Rust：体积和性能优秀，但团队开发复杂度更高。
- Electron：UI 开发快，但对本地文件安全事务不是最精简选择。
- PowerShell：适合验证原型，不适合长期维护的安全桌面产品。
