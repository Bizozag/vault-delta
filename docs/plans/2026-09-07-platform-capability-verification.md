# 跨平台文件系统能力验证记录

日期：2026-09-07

对应实施计划：Task 17、Phase Gate P5。

## 实现策略

平台名称和链接判断由 `WindowsFileSystemSemantics`、`MacOsFileSystemSemantics` 提供。是否可以安全执行事务不依赖平台名称推断，而由 `PlatformCapabilityProbe` 在用户选择的实际目标根和事务根上创建临时探针进行验证。

探针检查：

- 目标根可创建、flush 并删除临时文件。
- 事务根可创建、flush 并删除临时文件。
- 目标根不是符号链接、junction 或重解析点。
- 事务根到目标根支持目录移动，用于 Delete 备份和目录恢复。
- 目标目录内支持覆盖式文件移动，并验证替换后的内容。
- 记录实际卷的大小写敏感行为。
- 记录 NFC/NFD 文件名是保持独立、视为等价，还是由文件系统改变存储形式。
- 成功或失败后清理全部探针产物；仅在探针创建了空事务根时删除该空根。

## 工作流边界

Apply 的顺序为：补丁 Inspector、目标基线验证、平台能力验证、目标锁、payload 暂存、Journal 和目标变更。因此不支持的卷在锁、Journal、备份及目标内容写入之前阻断。

Rollback 在读取并验证 Journal 后，对目标根和该事务目录重新执行能力探测，再获取目标锁。能力改变或卷不可用时停止恢复，不执行猜测性文件操作。

## 自动化验证

- Windows 本地真实临时目录验证可写、目录移动、原子覆盖、大小写、Unicode、链接识别和探针清理。
- macOS CI 执行同一套测试，并使用 macOS 语义适配器。
- 固定 Unicode / Obsidian fixture 对序列化 manifest 计算黄金 SHA-256；Windows 和 macOS 必须得到同一值。
- E2E 注入不支持能力，验证 Apply 在锁、Journal 和目标写入前抛出 `PlatformNotSupportedException`。

## 尚需真实环境验收

- macOS APFS 默认卷与大小写敏感卷。
- macOS 外接 exFAT 卷。
- Windows NTFS 与独立盘符的跨卷事务根拒绝行为。
- Apple Silicon 真机上的完整生成、应用和回滚。

这些属于后续发布与路径验证阶段；代码和 CI 门禁已具备对应检测能力。
