# Vault Delta 测试策略

## 1. 测试金字塔

- Domain 单元测试：路径、差异分类、操作排序、不变量。
- Application 单元测试：工作流 Gate 与错误传播。
- Infrastructure 集成测试：真实临时目录、ZIP、哈希、原子替换。
- 端到端测试：生成补丁、传递补丁、应用、验证、回滚。
- 故障注入测试：在每个 Apply 步骤前后模拟异常。

## 2. 必测矩阵

### 路径

- 中文、Emoji、空格、组合 Unicode。
- NFC/NFD Unicode 规范化等价与冲突。
- 深层目录和超过 260 字符路径。
- 大小写碰撞。
- `..`、绝对路径、UNC、盘符。
- Windows 保留名称。
- 文件/目录同名类型变化。
- 符号链接、Finder alias（若可识别）、junction 和其他特殊文件系统条目。
- Windows 不区分大小写卷、macOS 大小写敏感与不敏感卷。

### 差异

- 纯新增、纯修改、纯删除。
- 唯一内容重命名。
- 多个相同内容文件导致重命名歧义。
- 重命名后再修改。
- 空文件和超大文件。
- 时间戳不同但内容相同。
- 时间戳相同但内容不同。

### 过滤

- include/exclude 优先级。
- Obsidian workspace 文件默认排除。
- `.obsidian/plugins/**` 与 `.obsidian/themes/**` 默认包含。
- `.trash/**` 默认排除。
- 两次扫描规则不一致时阻断。

### 打包

- 目录包和 ZIP。
- 空补丁。
- 输出空间不足。
- 复制期间源文件变化。
- ZIP 截断、重复项、清单外载荷。
- 同一补丁在 Windows 与 macOS 间交叉生成/检查/应用。

### 应用

- 基线完全匹配。
- 目标存在额外非冲突文件。
- 待覆盖文件已被独立修改。
- 待删除文件缺失。
- 目标文件锁定。
- 应用中空间耗尽。
- 进程在任意 operation 后崩溃。
- Apply 重复执行。
- macOS 外接卷、权限不足和不支持原子替换的卷。

### 回滚

- 新增文件撤销。
- 修改文件恢复。
- 删除文件恢复。
- 重命名撤销。
- 回滚中再次中断并重试。
- 备份缺失或损坏。

## 3. 属性测试

建议定义以下性质：

1. `Apply(Build(A, B), A) == B`，针对受管理范围。
2. `Rollback(Apply(Build(A, B), A)) == A`。
3. 相同输入重复 Compare 的 DiffSet 完全一致。
4. 任意合法 RelativePath 解析和序列化往返保持一致。
5. 任意非法路径永远不能解析为 RelativePath。
6. Diff 操作的目标路径唯一。

## 4. 固定样例库

在 `tests/fixtures/` 维护小型 Obsidian 风格库：

- Markdown 双向链接。
- Canvas JSON。
- Excalidraw Markdown。
- 图片与 PDF 占位文件。
- `.obsidian` 配置和插件目录。
- 各类重命名、删除和内容冲突。

样例不得包含真实私人笔记。

## 5. 性能测试

生成合成目录：

- 10,000 / 100,000 / 1,000,000 个小文件。
- 1 GB / 10 GB 单个大文件。
- 99.9% 不变、0.1% 变化。

记录枚举耗时、哈希吞吐、峰值内存和补丁大小。性能回归阈值初定为相同机器上超过基线 20%。

## 6. 发布门禁

Release 候选必须满足：

- 所有单元、集成和 E2E 测试通过。
- 无高危路径逃逸或数据丢失缺陷。
- Windows 10/11 各完成一次手工验收。
- `osx-arm64` 与 `osx-x64` 均完成 publish 和 `.app` 结构验证。
- 至少在真实 Apple Silicon Mac 上完成一次生成、应用、回滚和拖放交互验收。
- Intel 版本至少完成 macOS CI 构建与包检查；正式发布前优先在 Intel Mac 或受控兼容环境完成启动验收。
- 面向外部分发的 macOS 候选通过 Developer ID 签名、hardened runtime、公证和 Gatekeeper 验证。
- 使用真实副本库演练生成、应用和回滚。
- 文档中的 manifest schema 与实现测试样例一致。
