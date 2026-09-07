# ADR-0003：使用声明式补丁和事务式应用

## Status

Accepted

## Context

普通文件拖拽无法表达删除、重命名、基线验证和回滚。直接提供批处理脚本会增加命令注入和平台差异风险。

## Decision

补丁使用版本化 JSON manifest 描述操作，不包含可执行指令。受信任的 Vault Delta 应用器解释 manifest，先预检和备份，再逐操作写 Journal，失败时回滚。

## Consequences

### Positive

- 格式可审计、可测试、可版本化。
- 不需要执行补丁提供的脚本。
- 支持删除、重命名、冲突检查和恢复。

### Negative

- 精确补丁必须携带应用器或预先安装应用器。
- 实现复杂度高于简单复制目录。

## Alternatives Considered

- 仅输出 files 目录：不能保证最终一致。
- 生成 PowerShell/cmd 脚本：易受转义、编码和注入影响。
- 使用 Git bundle：要求把现有库纳入 Git，并不适合所有附件规模。
