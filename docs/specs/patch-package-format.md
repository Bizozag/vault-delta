# Vault Delta 补丁包格式 v1

## 1. 目录结构

```text
VaultDelta_<timestamp>_<short-id>/
  manifest.json
  README.txt
  files/
    <relative paths...>
```

ZIP 是默认传输形式，并必须完整包含同样的逻辑结构。目录形式用于检查、调试或受限环境。补丁是纯数据包，不嵌入平台相关可执行文件；目标端使用已安装或单独携带的 Vault Delta Desktop。MVP 不允许从 ZIP 内直接修改目标库，应用器先验证 ZIP，再流式解压载荷到受控 staging。

## 2. Manifest 最小结构

```json
{
  "schemaVersion": "1.0",
  "patchId": "01J...",
  "createdAtUtc": "2026-09-07T10:00:00Z",
  "generatorVersion": "0.1.0",
  "hashAlgorithm": "SHA-256",
  "mode": "exact",
  "rules": {
    "preset": "obsidian-default-v1",
    "includes": [],
    "excludes": []
  },
  "base": {
    "snapshotId": "sha256:...",
    "managedFileCount": 120000
  },
  "target": {
    "snapshotId": "sha256:...",
    "managedFileCount": 120016
  },
  "summary": {
    "added": 11,
    "modified": 9,
    "deleted": 4,
    "renamed": 2,
    "payloadBytes": 19503513
  },
  "operations": []
}
```

## 3. 操作模型

### Add

```json
{
  "sequence": 10,
  "type": "add",
  "path": "03-Projects/New.md",
  "payload": "files/03-Projects/New.md",
  "new": { "length": 1024, "sha256": "..." }
}
```

### Modify

```json
{
  "sequence": 20,
  "type": "modify",
  "path": "Assets/map.png",
  "old": { "length": 900, "sha256": "..." },
  "payload": "files/Assets/map.png",
  "new": { "length": 1100, "sha256": "..." }
}
```

### Delete

```json
{
  "sequence": 30,
  "type": "delete",
  "path": "Archive/obsolete.md",
  "old": { "length": 500, "sha256": "..." }
}
```

### Rename

```json
{
  "sequence": 40,
  "type": "rename",
  "from": "Notes/old-name.md",
  "to": "Notes/new-name.md",
  "old": { "length": 700, "sha256": "..." },
  "new": { "length": 700, "sha256": "..." }
}
```

若重命名同时修改内容，则必须表达为 rename 后 modify，或使用明确的 `renameModify` 新类型。v1 选择两个显式操作，减少格式复杂度。

## 4. 路径规范

- JSON 中统一使用 `/`。
- 必须为相对路径。
- 不允许空路径、`.`、`..` 段。
- 不允许以 `/`、`\\` 或盘符开头。
- 解码后再次规范化并验证。
- 为确保可移植到 Windows，Windows 保留名称和结尾空格/句点必须在任一平台的生成阶段阻断。
- 使用固定、与当前系统区域无关的大小写折叠规则后，路径必须唯一；源卷即使大小写敏感也不得放行碰撞。
- 规范化后出现 NFC/NFD 等价冲突的路径必须阻断，并在报告中列出原始名称。
- Manifest 不记录或依赖源端、目标端操作系统；同一数据包可由任一受支持平台检查与应用。

## 5. 稳定性与可复现

- operations 按规范路径序排序，同路径按固定操作优先级排序。
- JSON 属性输出顺序固定。
- 时间戳不参与 snapshotId。
- snapshotId 由规则标识和所有受管理条目的规范路径、类型、长度、内容哈希计算。
- 相同输入和规则应产生相同 snapshotId 与 operations。

## 6. 兼容策略

- `schemaVersion` 使用 major.minor。
- 应用器必须拒绝未知 major。
- 未知 minor 字段可忽略，但未知 operation type 必须拒绝。
- 清单中保留的扩展字段不得影响 v1 核心哈希定义，除非规范明确。

## 7. 信任模型

v1 只提供完整性校验，不提供发布者身份认证。未来可增加 `signature.json` 和公钥签名，但不得把普通 SHA-256 描述为数字签名。
