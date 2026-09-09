# Windows 与 macOS 继续工作指南

## 1. 从交接包恢复仓库

推荐使用交接包中的完整 Git bundle，而不是直接编辑 `source-snapshot`。

Windows PowerShell：

```powershell
git clone .\git\VaultDelta.repository.bundle VaultDelta
Set-Location .\VaultDelta
git status
git log -5 --oneline
```

macOS Terminal：

```bash
git clone ./git/VaultDelta.repository.bundle VaultDelta
cd VaultDelta
git status
git log -5 --oneline
```

预期分支为 `main`，工作区为空。`source-snapshot/` 是同一交接提交的普通文件副本，仅用于审阅、无 Git 环境恢复或对照。

建议接管后立即创建可信远程：

```bash
git remote add origin <repository-url>
git push -u origin main
```

不要把交接包中的凭据、Apple 证书或本机路径加入仓库。当前包本身不包含这些秘密。

## 2. 工具链

共同要求：

- Git。
- .NET SDK `10.0.101`，由 `global.json` 固定。
- PowerShell 7（`pwsh`），用于统一验证脚本。

macOS 额外要求：

- Xcode Command Line Tools。
- Bash、Python 3、`plutil`、`PlistBuddy`、`sips`、`iconutil`、`ditto`、`shasum`、`file`。
- Task 25 需要 Apple Developer Program、Developer ID Application 证书和 App Store Connect 公证凭据。

## 3. 每台机器接管后的第一组验证

Windows：

```powershell
dotnet --info
./scripts/verify.ps1 -Configuration Release
./scripts/publish-windows.ps1 -Version 0.1.1
```

如果 `artifacts/releases/windows` 已存在，先归档旧目录；打包脚本会拒绝静默覆盖。

macOS：

```bash
dotnet --info
pwsh ./scripts/verify.ps1 -Configuration Release
./scripts/publish-macos.sh --version 0.1.1
```

macOS 脚本会生成 `osx-arm64` 和 `osx-x64` 两个包；只对与当前主机架构一致的应用执行启动冒烟。检查：

```bash
cat artifacts/releases/macos/SHA256SUMS.txt
plutil -lint "artifacts/releases/macos/VaultDelta-0.1.1-osx-arm64/Vault Delta.app/Contents/Info.plist"
file "artifacts/releases/macos/VaultDelta-0.1.1-osx-arm64/Vault Delta.app/Contents/MacOS/VaultDelta"
file "artifacts/releases/macos/VaultDelta-0.1.1-osx-x64/Vault Delta.app/Contents/MacOS/VaultDelta"
```

## 4. 下一阶段：Task 25

目标：实现 Developer ID 签名、hardened runtime、公证、staple、Gatekeeper 检查和 DMG。

计划文件：

- 新建 `scripts/sign-and-notarize-macos.sh`。
- 新建 `.github/workflows/release.yml`，只在明确的 tag/manual release 流程中运行。
- 新建 `docs/release/macos-signing.md`。
- 需要时新增 entitlements 文件；遵循最小权限，不能为了通过签名关闭 library validation 或扩大文件访问权限。

建议验证顺序：

1. 从干净提交运行 Task 24 打包。
2. 导入临时 keychain 中的 Developer ID Application 证书。
3. 对 bundle 内原生 dylib、主 executable 和 `.app` 从内到外签名，启用 `--options runtime` 和 timestamp。
4. 执行 `codesign --verify --deep --strict --verbose=2`。
5. 用 `ditto` 生成公证 ZIP，提交 `xcrun notarytool submit --wait`。
6. `xcrun stapler staple` 与 `xcrun stapler validate`。
7. `spctl --assess --type execute --verbose=4`。
8. 生成 DMG，再对 DMG 校验/签名并记录 SHA-256。
9. 任意签名、公证或 Gatekeeper 步骤失败时不得发布候选包。

秘密建议只保存在 CI secret 或本机 Keychain，例如：证书 base64、证书密码、Apple ID/issuer/key 或 keychain profile。文档只能记录变量名，不能记录值。

## 5. Task 26 发布候选验收

Windows 10 与 Windows 11：

- 完整解压、启动、文件夹选择和拖放。
- 对 Obsidian 库副本生成 ZIP，只传输 ZIP 后在另一位置应用。
- 校验 `.obsidian/plugins/**`、主题、Markdown、Canvas、Excalidraw 和附件。
- 注入中断或使用测试场景验证 Journal 回滚。
- 验证长路径和外接卷失败前阻断。
- 记录 OS build、包哈希、结果和证据路径。

Apple Silicon Mac：

- 对原生 ARM64 `.app` 完成上述全链路。
- 验证 Finder 文件选择/拖放、Gatekeeper、签名、公证、staple。
- 覆盖 APFS 与至少一种外接卷（优先 exFAT）。

Intel Mac：

- 验证 x64 `.app` 启动、基础比较和补丁检查。
- 若无法取得真实 Intel 机器，必须在发布记录中明确未完成，不能把交叉构建当成启动验收。

最终更新 README、快速入门、平台限制与 release notes，并对三种 RID 记录版本、提交、哈希和验收状态。目标提交信息：

```text
build: sign and notarize macos releases
docs: prepare cross-platform mvp release
```

## 6. 每个阶段的停止边界

- 工作区不是干净状态：先识别已有修改归属，不覆盖或丢弃未知改动。
- 测试失败：不生成或发布候选包。
- 包内 `gitDirty` 为 `true`：只能作为开发中间包。
- 哈希不一致、ZIP 多顶层或路径越界：立即阻断。
- 基线冲突：不允许写目标库。
- 卷能力探测失败：不允许降级直接覆盖。
- 缺少 Apple 凭据：可以继续实现和无签名测试，但不能声称完成 Task 25。

## 7. 2026-09-09 后续行为基线

- 不恢复设置页、插件/Trash 单独开关或侧栏“本地模式”检测块，除非出现新的明确产品决策。
- 默认比较所有普通文件；安全路径、链接和卷能力 Gate 继续强制执行。
- Windows/macOS 发布候选必须从包含本次迭代的干净提交重新生成，历史交接包中的 Windows 二进制不代表当前功能。
