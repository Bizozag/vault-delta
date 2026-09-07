# Vault Delta

Vault Delta 是一个面向大型 Obsidian 笔记库的离线增量补丁工具。它比较两个目录快照，生成仅包含新增、修改、删除与重命名信息的可携带补丁包，并在目标端通过预检、备份、应用、校验和回滚完成安全更新。

MVP 面向 Windows 与 macOS，采用 C#、.NET 10 LTS 和 Avalonia。默认生成便于传输的 ZIP 补丁，同时保留目录包作为检查与调试选项。macOS 发布同时覆盖 Apple Silicon (`osx-arm64`) 和 Intel (`osx-x64`)；Metal 渲染由 Avalonia 的 macOS 后端提供，不单独维护“Metal 版”。

## 当前状态

项目处于设计与实施规划阶段，尚未包含可执行程序。

## 核心目标

- 比较旧快照与新快照，输出稳定、可复核的差异集合。
- 只传输新增和修改文件，保留相对目录结构。
- 用清单表达删除和重命名，避免旧文件残留。
- 应用前验证目标库基线，拒绝覆盖未经识别的目标端修改。
- 应用时先备份，失败时可回滚，完成后进行内容校验。
- 默认提供 Obsidian 友好的排除规则，但不擅自忽略整个 `.obsidian`。
- 默认包含 `.obsidian/plugins/**`，默认排除 `.trash/**`。

## 文档入口

- [产品范围与需求](docs/product-requirements.md)
- [系统架构](docs/architecture.md)
- [模块设计](docs/module-design.md)
- [端到端工作流与验证门禁](docs/workflows-and-validation.md)
- [补丁包规范](docs/specs/patch-package-format.md)
- [安全与失败恢复](docs/security-and-recovery.md)
- [测试策略](docs/testing-strategy.md)
- [实施计划](docs/plans/2026-09-07-vault-delta-implementation.md)
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

这些命令将在实施阶段创建解决方案后生效。
