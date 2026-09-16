# Vault Delta macOS 安装与使用

适用版本：Vault Delta 0.1.x 预览版

## 选择正确的发布包

- Apple Silicon（M1、M2、M3、M4 及后续芯片）：选择 `osx-arm64`。
- Intel Mac：选择 `osx-x64`。

两个包都包含 .NET 10 运行时，不需要另装 .NET。本阶段分别发布两个原生架构包，不合并为 Universal Binary；Avalonia 的 macOS 后端自动使用系统支持的 Metal/OpenGL 渲染，不存在单独的“Metal 版”。最低支持系统版本仍是正式发布前的验收 Gate，在完成目标机器矩阵测试前不写入未经验证的部署版本声明。

## 校验和安装

在“终端”中进入 ZIP 所在目录：

```bash
shasum -a 256 VaultDelta-0.1.8-osx-arm64.zip
cat SHA256SUMS.txt
```

两处 SHA-256 必须一致。然后完整解压 ZIP，把 `Vault Delta.app` 移到“应用程序”或其他本机目录。不要只复制 `Contents/MacOS/VaultDelta`，应用依赖 bundle 内的运行库和资源。

Task 24 预览包尚未进行 Developer ID 签名和 Apple 公证。只应运行可信来源且哈希正确的包。若 Gatekeeper 阻止启动，可在确认来源后于 Finder 中按住 Control 点按应用并选择“打开”。签名、公证、hardened runtime 和 DMG 属于 Task 25，正式外部分发时不应依赖此绕过步骤。

## 创建和传输补丁

1. 准备旧快照和更新后的新快照，两个目录不能互相包含。
2. 打开“比较快照”，从 Finder 把基线与目标文件夹拖入对应区域，或使用文件夹选择器。
3. 审核新增、修改、删除、重命名和风险项。
4. 生成 ZIP 补丁，并只传输这个补丁文件到目标 Mac。

默认比较全部普通文件，包括 `.obsidian/**`、`.trash/**` 和平台元数据文件；界面不提供特殊目录开关。路径与文件系统安全检查始终启用。

## 应用补丁和恢复

建议先关闭 Obsidian。打开“补丁与恢复”，从 Finder 拖入补丁 ZIP 和目标库（也可以使用选择器），再运行基线检查。schema、manifest、载荷、哈希和路径安全检查必须通过。若出现内容冲突，可逐项选择 `ignore`（保留目标文件并跳过该项更新）或 `revert`（采用更新包版本覆盖）；其他冲突仅支持 `ignore`。全部冲突处理后才可应用，写入前会再次检查目标状态。

Journal 和备份位于目标库同级目录：

```text
.<目标库目录名>.vaultdelta-transactions/<operation-id>/
  journal.json
  backup/
```

发生中断时，选择对应 `journal.json` 并运行恢复；恢复不依赖原补丁 ZIP。确认新库工作正常前不要删除事务目录。

Vault Delta 以相对路径、条目类型和文件内容 SHA-256 判断一致性，不同步文件夹修改时间，也忽略仅有时间戳的文件变化。默认纳管 `.obsidian/**`，因此扫描、应用和复核期间应保持 Obsidian 关闭；重新打开库后，`workspace.json`、recent-files 等运行状态再次变化属于新的本机写入。

## macOS 验收边界

- `osx-arm64` 和 `osx-x64` 都必须在 macOS runner 形成合法 `.app`、通过 plist、Mach-O 架构和 ZIP 结构检查。
- runner 只启动与其主机架构一致的包；Intel 包仍需 Intel Mac 或受控 Rosetta/Intel 环境补充人工启动验收。
- 正式候选还要在真实 Apple Silicon Mac 验证文件选择、拖放、补丁生成、应用、回滚和外接卷能力探测。
- APFS、HFS+、exFAT 或网络卷的语义不同；能力探测失败时程序会在写入前阻断，不会降级为直接覆盖。
- 当前不提供公开命令行应用模式。
