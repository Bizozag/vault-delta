# 交接包内容说明

交接包由 `scripts/create-handoff-package.ps1` 从干净 Git 提交生成，默认输出到仓库父目录，避免把包自身递归打包。

结构：

```text
VaultDelta-Handoff-<timestamp>-<commit>/
  START_HERE.md
  HANDOFF_METADATA.json
  SHA256SUMS.txt
  git/
    VaultDelta.repository.bundle
  source-snapshot/
    ...当前提交的完整受版本管理源码...
  release-artifacts/
    windows/...
    macos/...（只有本机已经生成时才存在）
  evidence/
    git-log.txt
    git-status.txt
    tracked-files.txt
```

说明：

- `git/VaultDelta.repository.bundle`：完整 Git 历史与分支，可在 Windows/macOS 直接 clone。
- `source-snapshot/`：当前交接提交的普通源码副本，不含 `.git`、`bin`、`obj` 或其他忽略产物。
- `release-artifacts/`：只收集明确的最终候选发布目录，不复制名称含 `intermediate` 的中间包。
- `SHA256SUMS.txt`：UTF-8 编码，记录交接目录内每个文件的 SHA-256；校验文件本身除外，支持中文和其他 Unicode 路径。
- 同级 `.zip`：便于文件传输；同级 `.zip.sha256` 校验整个交接 ZIP。

恢复时优先使用 Git bundle。若只需要运行 Windows 预览包，可直接从 `release-artifacts/windows` 取出候选 ZIP并核对其原始 `SHA256SUMS.txt`。
