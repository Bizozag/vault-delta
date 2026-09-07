# Apply / Rollback 实施与验证记录

日期：2026-09-07

对应实施计划：Task 15、Task 16、Phase Gate P4。

## 已实现边界

- Apply 无条件检查补丁包并验证目标基线；冲突时不创建锁、Journal 或事务目录。
- ZIP 与目录包的 payload 均物化到受控临时目录，并在使用前再次核对长度和 SHA-256。
- Apply 获取目标锁后创建 `Prepared` Journal，并逐操作执行即时基线复核、备份、变更和结果验证。
- Add、Modify、Delete、Rename 同时支持文件和当前 manifest 可表达的目录操作。
- 任何已捕获失败都会停止后续操作，将 Journal 标记为 `NeedsRollback` 并保留备份。
- Rollback 不依赖原补丁包，按 Journal 逆序恢复，并在每个动作后验证旧状态。
- Rollback 可从 `Prepared`、`Applying`、`Verifying`、`NeedsRollback`、`Committed` 或中断的 `RollingBack` 恢复。
- 已完成的回滚可重复调用；中断在“逆向动作后、Journal 更新前”也能安全续跑。
- 缺少备份时停止在 `RollingBack`，保留目标现状，不猜测或继续后续恢复。

## 故障窗口

当前可注入检查点：

1. `AfterPreparedJournal`
2. `AfterBackup`
3. `AfterTargetMutation`
4. `AfterOperationJournal`
5. `BeforeFinalVerification`
6. `AfterRollbackMutation`

Delete 操作在移动旧条目前先持久化确定性的备份相对路径。Modify 先复制并验证旧文件，再记录 `BackupCompleted`。Add 和 Rename 即使在目标变更后、操作状态更新前中断，也通过目标旧/新状态判定是否需要逆向动作。

## 自动化验证

端到端测试覆盖：

- 全操作组合 Apply 后与目标树一致。
- 同一事务 Rollback 后与原始树一致。
- Rollback 幂等重试。
- Apply 在备份后与目标变更后中断。
- Rollback 在逆向变更后中断并续跑。
- 基线冲突零写入。
- ZIP payload 暂存、校验和应用。
- 备份缺失时安全停止。

验证命令：

```powershell
./scripts/verify.ps1 -Configuration Release
```
