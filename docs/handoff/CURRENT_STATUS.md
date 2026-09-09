# Vault Delta 当前交接状态

更新时间：2026-09-08（Asia/Shanghai）

## 1. 项目目标与固定决策

Vault Delta 用于比较两个大型 Obsidian 库快照，生成只包含新增、修改、删除与重命名信息的离线增量 ZIP，并在目标库执行基线检查、事务应用和可重试回滚。

已经确认且不应在后续实现中无故改变的决策：

- C#、.NET 10 LTS、Avalonia 12，Windows/macOS 共用核心与桌面 UI。
- 默认纳管全部普通文件，包括 `.obsidian/**`、`.trash/**` 和平台元数据文件，不提供特殊目录开关。
- 默认补丁格式为 ZIP，同时保留目录 writer 供检查和调试。
- MVP 不增加公开 CLI；构建和发布脚本不属于面向最终用户的 CLI 产品面。
- Windows MVP 为 `win-x64` 自包含便携 ZIP，不做 MSI/MSIX。
- macOS 分别提供 `osx-arm64` 和 `osx-x64`，不额外维护“Metal 版”；Metal/OpenGL 由 Avalonia macOS 后端选择。
- 路径越界、链接/重解析点、基线冲突、载荷损坏或卷能力不足时必须在写入前阻断。

## 2. Git 状态

交接文档生成前的关键提交：

```text
cfb4f7c build: package macos desktop releases
0aa15f9 build: package windows desktop release
3022126 test: establish large vault performance baseline
2f9822e test: add obsidian vault end-to-end fixtures
5ae8603 feat: expose transactional patch workflows
5f60367 feat: add interactive diff review
221b088 feat: add desktop application shell
f48bf58 feat: validate cross-platform filesystem capabilities
c320c46 feat: rollback interrupted patch applications
3c759c4 feat: apply patches transactionally
```

当前主分支为 `main`。截至交接时没有配置 Git remote。交接包因此包含完整 `repository.bundle`，它是跨机器继续开发的首选入口。

## 3. 已完成阶段

| 阶段 | 状态 | 主要证据 |
|---|---|---|
| Task 1–17 核心与安全模型 | 完成 | 路径、快照、哈希、规则、Diff、补丁、检查、Apply、Rollback、平台能力测试 |
| Task 18–20 桌面体验 | 完成 | Avalonia 应用壳、差异审核、补丁生成、应用与恢复页面 |
| Task 21 固定 Obsidian E2E | 完成 | Markdown、Canvas、Excalidraw、附件、插件和主题 fixture |
| Task 22 性能与路径 | 完成 | 10 万文件发布测量和跨平台路径/卷测试框架 |
| Task 23 Windows 发布 | 完成代码与本机候选包 | `win-x64` self-contained ZIP、188/188 测试、启动冒烟、哈希 |
| Task 24 macOS 双架构包 | 完成代码，等待 macOS 环境验收 | 双 RID publish、`.app`、plist、图标、ZIP、CI 包检查与原生架构冒烟 |
| Task 25 macOS 签名/公证/DMG | 未开始 | 需要 Apple Developer 证书和公证凭据 |
| Task 26 发布候选验收文档 | 未开始 | 依赖真实 Windows 10/11、Apple Silicon/Intel 验收结果 |

### 2026-09-09 界面与默认范围迭代

- 设置页和侧栏“本地模式”状态块已移除。
- 默认规则改为空规则，全部普通文件参与扫描、补丁、应用与恢复。
- ZIP 生成现在展示准备、复制校验、写清单、压缩、重新验证、发布阶段及确定百分比和当前路径。
- 旧的 Windows `0.1.0` 候选包构建于该迭代之前，只能用于历史对照；合入本次提交后必须重新打包并记录新哈希。

## 4. 最近验证结果

### 完整测试

- Release build：0 警告、0 错误。
- 测试：190/190 通过。
- 固定 E2E：补丁生成、检查、应用和回滚均覆盖。

### 大库性能基线

- 10 万文件生成：约 23.8 秒。
- 双快照扫描、哈希和比较：约 5 分 39.5 秒。
- 当前瓶颈是安全的逐文件哈希；没有为性能绕过稳定性或内容校验。

### Windows 候选包

```text
Commit: 0aa15f96252c6e97525d6498861d045a61f694bf
Version: 0.1.0
RID: win-x64
Files: 227
Uncompressed: 214,020,958 bytes
ZIP: 75,506,719 bytes
SHA-256: 790259455f489739f96ae074c8335078b48b877df06773939e8d1f1d79e4f0de
gitDirty: false
signatureStatus: NotSigned
```

隐藏启动 3 秒冒烟通过，ZIP 只有 `VaultDelta-0.1.0-win-x64/` 一个顶层目录，无路径越界条目。该预览包未做 Authenticode 签名。

### macOS 交叉发布

Windows 开发机已分别执行 `dotnet publish`：

- `osx-arm64`：222 个文件，约 115,814,065 bytes，主程序为 ARM64 Mach-O。
- `osx-x64`：222 个文件，约 108,930,483 bytes，主程序为 x86-64 Mach-O。

这只能证明交叉发布和静态文件正确，不能替代 macOS 原生启动、Gatekeeper、签名、公证或 Finder 拖放验收。macOS CI 工作流已加入仓库，但交接时没有配置远程仓库，尚未产生 runner 结果。

## 5. 已知边界与风险

- 没有 Git remote；首次接管后应建立可信远程并推送完整历史。
- Windows 包未签名，SmartScreen 可能提示；必须校验 SHA-256。
- macOS 包尚未在真实 Mac 上由当前交接流程生成，也未签名、公证或制作 DMG。
- macOS 最低支持版本仍待真实机器矩阵确认，`Info.plist` 未写入未经验证的最低版本。
- Intel Mac 启动验收尚未完成；至少需要 macOS CI 静态检查，正式发布前优先使用真实 Intel Mac 或受控兼容环境。
- 外接 APFS/HFS+/exFAT/网络卷仍需要人工矩阵验收。能力不足时应用流程应在写入前阻断。
- Windows 10 与 Windows 11 的独立机器人工验收尚需记录。
- `artifacts/releases/windows-intermediate-20260908` 是提交前中间包，不是发布候选，不应对外分发。

## 6. 重要入口

- 总实施路线：`docs/plans/2026-09-07-vault-delta-implementation.md`
- 架构：`docs/architecture.md`
- 安全与恢复：`docs/security-and-recovery.md`
- 测试策略：`docs/testing-strategy.md`
- Windows 发布记录：`docs/plans/2026-09-08-windows-portable-release.md`
- macOS Task 24 计划：`docs/plans/2026-09-08-macos-dual-architecture-release.md`
- Windows 使用指南：`docs/user-guide/windows-installation.md`
- macOS 使用指南：`docs/user-guide/macos-installation.md`
- Windows 打包：`scripts/publish-windows.ps1`
- macOS 打包：`scripts/publish-macos.sh`
- 完整验证：`scripts/verify.ps1`
