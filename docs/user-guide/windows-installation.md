# Vault Delta Windows 安装与使用

适用版本：Vault Delta 0.1.x 预览版
目标系统：Windows 10 64 位、Windows 11 64 位

## 发布包说明

Windows 发布物是 `win-x64` 自包含 ZIP。它已经包含 .NET 10 运行时，目标电脑不需要另外安装 .NET，也不需要管理员权限。

发布目录包含：

```text
VaultDelta-<version>-win-x64/
  VaultDelta.exe
  README.txt
  release.json
```

`VaultDelta.exe` 是包含 .NET、Avalonia 和所有运行依赖的单文件主程序。运行时需要的本机库会解压到当前用户的临时运行缓存，不会把 DLL 散落到发布目录。

同级还会生成：

```text
VaultDelta-<version>-win-x64.zip
SHA256SUMS.txt
```

当前预览包尚未进行 Windows 代码签名。首次启动时 Windows 可能显示 SmartScreen 提示。请只使用可信渠道获得的 ZIP，并先校验 SHA-256。

## 校验下载文件

在 PowerShell 中进入 ZIP 所在目录：

```powershell
Get-FileHash .\VaultDelta-0.1.4-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

两处 SHA-256 必须完全一致。若不一致，不要解压或运行。

## 安装与首次启动

1. 将 ZIP 解压到本机普通目录，例如 `D:\Tools\VaultDelta-0.1.4-win-x64`。
2. 不要直接从 ZIP 内运行；完整解压后，根目录中的 `VaultDelta.exe` 就是启动入口。
3. 双击 `VaultDelta.exe`。
4. 程序以当前用户权限运行，不会请求管理员权限。
5. 如果 SmartScreen 阻止未签名预览包，确认哈希和来源后选择“更多信息”→“仍要运行”。

建议不要安装到 Obsidian 库内部，避免工具自身文件进入快照。

## 创建和传输补丁

1. 准备旧快照和更新后的新快照，二者必须是独立目录，不能互相包含。
2. 打开“创建更新包”，分别把基线和目标文件夹拖入对应区域，或使用“选择…”按钮。
3. 开始比较并审核新增、修改、删除、重命名及风险项。
4. 选择“生成 ZIP 更新包”。最终 ZIP 只有在 manifest 和所有载荷重新校验成功后才会发布。
5. 把这个补丁 ZIP 复制到移动硬盘、局域网传输目录或目标 Windows 电脑。无需传输完整 Obsidian 库。

默认比较全部普通文件，包括 `.obsidian/**`、`.trash/**` 和平台元数据文件；界面不提供特殊目录开关。路径与文件系统安全检查始终启用。

## 在目标电脑应用补丁

1. 建议关闭 Obsidian，或确保没有其他程序正在写入目标库。
2. 打开“应用更新包”。
3. 把更新 ZIP 拖入更新包区域，或点击“打开更新包”。程序先检查 schema、manifest、载荷、哈希和包路径安全。
4. 把目标 Obsidian 库文件夹拖入目标区域，或点击“选择目标库”。
5. 执行“检查基线”。有任何冲突时，“应用更新包”保持禁用，目标库不会被写入。
6. Gate 全部通过后选择“应用更新包”。
7. 程序会先保存 Journal 和必要备份，再逐项更新并验证。

事务数据位于目标库同级隐藏目录：

```text
.<目标库目录名>.vaultdelta-transactions\<operation-id>\
  journal.json
  backup\
```

确认新库工作正常前，不要删除该目录。

## 中断与回滚演练

应用发生中断时，界面会显示 `NeedsRollback` 并自动保留 Journal 路径。恢复不需要原补丁 ZIP。

手动恢复步骤：

1. 打开“应用更新包”。
2. 在右侧选择对应的 `journal.json`。
3. 选择“开始恢复”。
4. 程序按相反顺序恢复备份并逐项验证。
5. 如果恢复再次中断，使用同一 Journal 重试；不要手动混合移动 backup 中的文件。

建议在发布验收时使用 Obsidian 库副本完成一次演练：

1. 对旧副本生成补丁并成功应用。
2. 打开 Obsidian，抽查 Markdown、Canvas、Excalidraw、附件、插件和主题。
3. 关闭 Obsidian。
4. 使用 Journal 回滚。
5. 确认旧副本逐文件恢复。

## 更新和卸载

更新 Vault Delta 时，将新版本 ZIP 解压到新目录，不要覆盖正在运行的旧目录。确认新版本可用后，可以删除旧应用目录。

Vault Delta 本身没有系统级安装项。卸载只需删除应用目录。目标库同级的 `.vaultdelta-transactions` 目录属于恢复资料；只有在确认不再需要回滚后才应单独删除。

## 已知边界

- 仅支持 Windows x64；Windows ARM64 尚未发布原生包。
- 当前包未签名，正式对外发布前应增加 Authenticode 签名流程。
- 不提供公开命令行应用模式。
- 不跟随符号链接、目录联接或重解析点。
- 外接卷必须通过应用前能力探测；不支持原子替换或同卷移动时会在写入前阻断。
