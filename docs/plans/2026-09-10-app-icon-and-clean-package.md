# 应用图标与精简 Windows 发布包

日期：2026-09-10
目标版本：0.1.4

## 目标

- 为 Windows 主程序嵌入可辨识的 Vault Delta 品牌图标。
- 避免自包含运行时的 DLL、JSON 和本机库散落在发布目录根部。
- 保持解压即用，不引入安装程序或额外启动器。

## 设计

- 图标延续应用内的青绿色圆角方块和白色双向箭头。
- `generate-app-icon.ps1` 从确定性矢量几何生成 1024px PNG 和包含 16–256px 多帧的 ICO。
- Windows 项目通过 `ApplicationIcon` 将 ICO 嵌入 apphost EXE。
- macOS 图标生成复用同一 1024px PNG，避免两个平台继续使用不同品牌图形。
- Windows 发布切换到 .NET 自包含单文件模式，并启用本机依赖自解压和程序集压缩。
- 发布根目录固定为 `VaultDelta.exe`、`README.txt`、`release.json`；脚本发现额外条目时直接失败。

## 验证边界

- 普通 Release 构建和单文件发布均必须成功。
- 从发布 EXE 提取的关联图标必须包含足量品牌青绿色像素。
- 隐藏启动 3 秒内不得退出。
- ZIP 顶层只有版本目录；版本目录内恰好三个文件，不含子目录。
- `release.json` 必须声明 `self-contained-single-file`、干净提交和未签名状态。
- SHA-256 必须与 `SHA256SUMS.txt` 一致。
