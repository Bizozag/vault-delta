# Windows 便携式发布设计与验证记录

日期：2026-09-08

对应实施计划：Task 23。

## 发布形式

Windows MVP 采用 `win-x64` 自包含目录，并将完整目录封装为 ZIP。该方案不依赖目标电脑预装 .NET，不需要管理员权限，也不引入 MSI/MSIX 安装器的升级、卸载和签名维护成本。

发布物结构：

```text
artifacts/releases/windows/
  VaultDelta-<version>-win-x64/
    VaultDelta.exe
    VaultDelta.dll
    VaultDelta.deps.json
    VaultDelta.runtimeconfig.json
    .NET 10 与 Avalonia 运行库
    README.txt
    release.json
  VaultDelta-<version>-win-x64.zip
  SHA256SUMS.txt
```

`artifacts/` 已在 Git 中排除。历史发布目录和 ZIP 不会被脚本静默覆盖；重复使用同一版本打包时必须先明确归档或移除旧产物。

## 项目元数据和 Windows manifest

共享构建属性固定产品名、作者和 `0.1.0` 版本入口。桌面程序集名称为 `VaultDelta`，因此用户入口是 `VaultDelta.exe`。

Windows manifest 声明：

- `asInvoker`：使用当前用户权限，不请求管理员权限。
- Windows 10+ compatibility GUID。
- Per-Monitor V2 DPI awareness。
- `longPathAware`，允许应用使用现代 Windows 长路径能力。

Debug 和 Release 均不依赖不可用的 Avalonia Developer Tools 扩展；日志仍通过 `LogToTrace()` 保留。

## 打包流程

`scripts/publish-windows.ps1` 执行：

1. 默认运行完整 Release format、build、test 验证。
2. 执行 `dotnet publish -r win-x64 --self-contained true`。
3. 检查 EXE、程序集、runtimeconfig、hostfxr、coreclr 和 Avalonia 关键文件。
4. 写入包内 README 和 `release.json`。
5. 隐藏启动 `VaultDelta.exe`，运行 3 秒后确认进程仍存活，再终止冒烟实例。
6. 原子移动临时发布目录到最终目录。
7. 生成 ZIP 和 `SHA256SUMS.txt`。

`release.json` 记录版本、RID、框架、Git commit、工作区是否 dirty 和 Authenticode 状态。正式发布应从干净提交构建；本地开发中间包会明确显示 `gitDirty: true`。

## 本机验证结果

首次本机 Windows 发布验证：

```text
RID：win-x64
模式：self-contained
应用版本：0.1.0.0
文件数：227
未压缩大小：214,020,901 bytes
ZIP 大小：75,506,666 bytes
隐藏启动冒烟：通过
Release 测试：188/188 通过
构建警告：0
构建错误：0
```

首次中间包 SHA-256 为：

```text
da7a51f8d9fb5c40237b1fc0683e54647c790a3315703d82073725c6b3369c33
```

该哈希属于提交前的中间验证包，不作为最终发布哈希。提交完成后需要重新运行脚本，由新生成的 `SHA256SUMS.txt` 作为本地候选包依据。

## 安装、应用和恢复验收

用户指南位于 `docs/user-guide/windows-installation.md`，覆盖：

- SHA-256 校验。
- 完整解压和首次启动。
- 从两份快照生成 ZIP。
- 通过移动介质或局域网传输补丁。
- 目标基线 Gate、事务应用和 Journal 位置。
- 应用中断后的独立恢复。
- 使用 Obsidian 库副本执行应用和回滚演练。
- 更新与卸载。

真实补丁应用和回滚算法已由固定 Obsidian E2E fixture 覆盖；本阶段额外验证发布后的真实 EXE 可以在当前 Windows 主机启动。

## 尚未完成的发布 Gate

- 当前主机只证明当前 Windows 环境启动成功，尚未分别记录独立 Windows 10 和 Windows 11 机器的人工验收结果。
- 当前预览包没有 Authenticode 签名，可能触发 SmartScreen。用户指南明确要求验证 SHA-256。
- Windows ARM64 不在 MVP 发布矩阵中。
- 安装器、自动更新和文件关联均不在 Task 23 范围。
