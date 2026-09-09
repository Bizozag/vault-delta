# Vault Delta

Vault Delta 是一个面向大型 Obsidian 笔记库的离线增量补丁工具。它比较两个目录快照，生成仅包含新增、修改、删除与重命名信息的可携带补丁包，并在目标端通过预检、备份、应用、校验和回滚完成安全更新。

MVP 面向 Windows 与 macOS，采用 C#、.NET 10 LTS 和 Avalonia。默认生成便于传输的 ZIP 补丁，同时保留目录包作为检查与调试选项。macOS 发布同时覆盖 Apple Silicon (`osx-arm64`) 和 Intel (`osx-x64`)；Metal 渲染由 Avalonia 的 macOS 后端提供，不单独维护“Metal 版”。

## 当前状态

核心 MVP 工作流、Avalonia 桌面界面、固定 Obsidian E2E fixture、性能基线以及 Windows/macOS 发布脚本已经实现。当前进入跨平台发布收尾阶段：Windows `win-x64` 候选包已完成本机验证；macOS `osx-arm64` 与 `osx-x64` 应用包和 CI 门禁已实现，仍需在 macOS runner/真实 Mac 上执行原生验收，并继续完成 Developer ID 签名、公证和 DMG。

## 核心目标

- 比较旧快照与新快照，输出稳定、可复核的差异集合。
- 只传输新增和修改文件，保留相对目录结构。
- 用清单表达删除和重命名，避免旧文件残留。
- 应用前验证目标库基线，拒绝覆盖未经识别的目标端修改。
- 应用时先备份，失败时可回滚，完成后进行内容校验。
- 默认比较并恢复全部普通文件，包括 `.obsidian/**` 与 `.trash/**`；安全路径和链接检查始终强制执行。
- 支持从 Explorer/Finder 拖入旧快照、新快照、应用目标库和 ZIP 补丁；拖放不会绕过完整性或基线检查。

## 文档入口

- [产品范围与需求](docs/product-requirements.md)
- [系统架构](docs/architecture.md)
- [模块设计](docs/module-design.md)
- [端到端工作流与验证门禁](docs/workflows-and-validation.md)
- [补丁包规范](docs/specs/patch-package-format.md)
- [安全与失败恢复](docs/security-and-recovery.md)
- [测试策略](docs/testing-strategy.md)
- [实施计划](docs/plans/2026-09-07-vault-delta-implementation.md)
- [当前交接状态](docs/handoff/CURRENT_STATUS.md)
- [跨平台继续工作指南](docs/handoff/CONTINUATION_GUIDE.md)
- [架构决策记录](docs/adr/README.md)

## 设计原则

1. **默认停止而不是猜测**：路径越界、基线不匹配、哈希异常或空间不足都必须阻断流程。
2. **生成与应用分离**：差异计算和补丁生成不能修改旧快照、新快照或目标库。
3. **内容是真相**：时间戳和大小用于快速筛选，最终正确性由内容哈希确认。
4. **可恢复优先**：删除实际执行为移动到备份区，确认成功后再由用户决定是否清理。
5. **格式可审计**：补丁清单使用有版本号的 JSON，便于人工检查和后续兼容。

## 建议开发命令

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

完整本地门禁：

```powershell
./scripts/verify.ps1 -Configuration Release
```
