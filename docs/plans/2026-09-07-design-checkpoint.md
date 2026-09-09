# Vault Delta 设计检查点

## 已确认

- 产品解决的是离线增量传输，而不是实时同步。
- 需要保留目录结构。
- 需要同时覆盖新增、修改、删除和重命名。
- 应用目标库时必须可验证和可恢复。
- 当前项目路径为 `D:\Vault\_Delta`。
- 2026-09-09 后续决策：默认纳管全部普通文件，包括 `.obsidian/**` 与 `.trash/**`；本条取代最初的 Trash 排除决策。
- ZIP 默认输出，目录包作为高级/调试选项。
- MVP 不开发公开 CLI。
- 正式支持 Windows 和 macOS。

## 当前默认决策

- C#、.NET 10 LTS + Avalonia。
- 模块化单体，核心逻辑与 UI 解耦。
- SHA-256 是正式一致性依据。
- 声明式 JSON manifest，不生成任意执行脚本。
- 精确应用默认因任何受影响路径冲突而停止。
- v1 不跟随符号链接、Windows 重解析点或其他特殊文件系统条目。
- macOS 发布 `osx-arm64` 与 `osx-x64`。
- macOS 的 Metal 渲染由 Avalonia 后端提供，不维护独立“Metal 版”。

## 发布前仍需确认

1. 必须支持的 Windows 最低版本。
2. 必须支持的 macOS 最低版本。
3. 是否已有 Apple Developer Program 账号及 Developer ID 证书。
4. 是否要求 Windows 绿色免安装包之外再提供安装器。
5. 是否需要在 MVP 后补充 Windows ARM64。

这些选择不会改变核心安全模型，但会影响 UI 优先级和发布包装。
