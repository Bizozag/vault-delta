# ADR-0004：v1 不跟随符号链接和重解析点

## Status

Accepted

## Context

Windows junction、符号链接和其他重解析点可能把扫描或写入引向快照根和目标根之外，造成意外读取、无限递归或覆盖外部文件。

## Decision

MVP 扫描到重解析点时记录并默认阻断；用户可通过规则明确排除该路径。Apply 不创建或跟随重解析点。

## Consequences

### Positive

- 明确限制路径逃逸风险。
- 简化跨平台语义和回滚。

### Negative

- 使用链接组织附件的笔记库需要调整或排除。

## Alternatives Considered

- 跟随并检查最终路径：仍有竞态和循环复杂度。
- 复制链接本身：各平台权限与语义不一致，留待后续版本。
