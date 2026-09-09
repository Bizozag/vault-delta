# 全文件快照与 ZIP 进度迭代记录

日期：2026-09-09

## 用户决策

- 移除设置页，不再暴露插件、主题或 `.trash` 的单独控制。
- 默认比较所有普通文件，包括 `.trash/**`、Obsidian workspace 文件和平台元数据文件。
- 移除侧栏左下角“本地模式 / 无网络 / 无遥测”展示。
- 比较完成后生成 ZIP 时，界面展示阶段、确定百分比和当前文件。

该决策取代此前文档中的 Obsidian 默认排除规则。路径越界、符号链接/重解析点、大小写/Unicode 碰撞、源目录变化、包完整性、基线 Gate 和卷能力检查属于安全不变量，不是可配置过滤项，继续强制执行。

## 实现模块

1. `ObsidianDefaultRules` 与嵌入 preset 保留稳定名称，但规则集合为空，因此所有普通文件默认参与扫描。
2. `PackageBuildProgress` 提供准备、复制载荷、写清单、压缩、重新验证、发布和完成阶段。
3. `DirectoryPackageWriter` 报告载荷复制与校验进度；`ZipPackageWriter` 使用逐文件 `ZipArchive` 写入替代无进度的整体压缩 API，并将最终验证纳入 100% 之前。
4. Desktop 服务透传进度；比较 ViewModel 提供百分比、阶段文案和当前路径；比较页仅在生成中显示确定进度条。
5. 应用壳只保留“比较快照”和“补丁与恢复”两页。

## 验证边界

- 默认规则测试必须证明 `.trash`、workspace、插件、主题和普通文件均被纳管。
- 固定 Obsidian E2E 必须把 Trash 与 workspace 变化写入 ZIP、应用到目标并通过 Journal 回滚。
- snapshot/manifest 黄金 SHA-256 必须更新并保持跨平台确定性。
- ZIP writer 测试必须观察复制、压缩、验证和完成阶段，进度单调不下降且最终为 100%。
- ViewModel 测试必须在阻塞中的压缩阶段暴露百分比、当前文件与阶段文案。
- Headless Avalonia 测试必须证明设置页/入口已不存在，两页导航仍可用。
- 完整 Debug 与 Release 测试通过后才提交；已有 Windows/macOS 应用候选需要基于新提交重新打包，不得沿用旧哈希作为当前版本。
