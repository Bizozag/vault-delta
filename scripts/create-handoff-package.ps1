[CmdletBinding()]
param(
    [string]$OutputRoot,
    [switch]$SkipVerify
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$repositoryParent = Split-Path -Parent $repositoryRoot
$resolvedOutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $repositoryParent 'handoff-packages'
}
else {
    [System.IO.Path]::GetFullPath($OutputRoot, $repositoryRoot)
}

$status = git -C $repositoryRoot status --porcelain
if (-not [string]::IsNullOrWhiteSpace(($status -join [Environment]::NewLine))) {
    throw 'Handoff packages must be generated from a clean Git worktree. Commit or intentionally resolve all changes first.'
}

if (-not $SkipVerify) {
    & (Join-Path $PSScriptRoot 'verify.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "Repository verification failed with exit code $LASTEXITCODE."
    }
}

$commit = (git -C $repositoryRoot rev-parse HEAD).Trim()
$shortCommit = (git -C $repositoryRoot rev-parse --short=8 HEAD).Trim()
$branch = (git -C $repositoryRoot branch --show-current).Trim()
$timestamp = [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss')
$packageName = "VaultDelta-Handoff-$timestamp-$shortCommit"
$packageDirectory = Join-Path $resolvedOutputRoot $packageName
$archivePath = Join-Path $resolvedOutputRoot "$packageName.zip"

if (Test-Path -LiteralPath $packageDirectory) {
    throw "Handoff directory already exists: $packageDirectory"
}
if (Test-Path -LiteralPath $archivePath) {
    throw "Handoff archive already exists: $archivePath"
}

New-Item -ItemType Directory -Path $packageDirectory | Out-Null
$gitDirectory = New-Item -ItemType Directory -Path (Join-Path $packageDirectory 'git')
$sourceDirectory = New-Item -ItemType Directory -Path (Join-Path $packageDirectory 'source-snapshot')
$evidenceDirectory = New-Item -ItemType Directory -Path (Join-Path $packageDirectory 'evidence')
$releaseDirectory = New-Item -ItemType Directory -Path (Join-Path $packageDirectory 'release-artifacts')
$temporaryArchive = Join-Path ([System.IO.Path]::GetTempPath()) "vaultdelta-source-$([Guid]::NewGuid().ToString('N')).zip"

try {
    $bundlePath = Join-Path $gitDirectory.FullName 'VaultDelta.repository.bundle'
    git -C $repositoryRoot bundle create $bundlePath --all
    if ($LASTEXITCODE -ne 0) {
        throw "git bundle failed with exit code $LASTEXITCODE."
    }
    git bundle verify $bundlePath | Out-File -LiteralPath (Join-Path $evidenceDirectory.FullName 'git-bundle-verify.txt') -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) {
        throw "git bundle verification failed with exit code $LASTEXITCODE."
    }

    git -C $repositoryRoot archive --format=zip --output=$temporaryArchive HEAD
    if ($LASTEXITCODE -ne 0) {
        throw "git archive failed with exit code $LASTEXITCODE."
    }
    Expand-Archive -LiteralPath $temporaryArchive -DestinationPath $sourceDirectory.FullName

    git -C $repositoryRoot log --oneline --decorate --graph --all | Out-File -LiteralPath (Join-Path $evidenceDirectory.FullName 'git-log.txt') -Encoding utf8NoBOM
    git -C $repositoryRoot status --short --branch | Out-File -LiteralPath (Join-Path $evidenceDirectory.FullName 'git-status.txt') -Encoding utf8NoBOM
    git -C $repositoryRoot ls-files | Out-File -LiteralPath (Join-Path $evidenceDirectory.FullName 'tracked-files.txt') -Encoding utf8NoBOM

    $windowsRelease = Join-Path $repositoryRoot 'artifacts/releases/windows'
    if (Test-Path -LiteralPath $windowsRelease -PathType Container) {
        Copy-Item -LiteralPath $windowsRelease -Destination (Join-Path $releaseDirectory.FullName 'windows') -Recurse
    }
    $macRelease = Join-Path $repositoryRoot 'artifacts/releases/macos'
    if (Test-Path -LiteralPath $macRelease -PathType Container) {
        Copy-Item -LiteralPath $macRelease -Destination (Join-Path $releaseDirectory.FullName 'macos') -Recurse
    }

    $startHere = @"
# Vault Delta 跨平台交接包

生成时间：$([DateTimeOffset]::Now.ToString('O'))
分支：$branch
提交：$commit

请先阅读：

1. `source-snapshot/docs/handoff/CURRENT_STATUS.md`
2. `source-snapshot/docs/handoff/CONTINUATION_GUIDE.md`
3. `source-snapshot/docs/plans/2026-09-07-vault-delta-implementation.md`

继续开发的首选方式：

```text
git clone git/VaultDelta.repository.bundle VaultDelta
```

`source-snapshot` 是无 Git 恢复备用。`release-artifacts` 只包含生成时存在的正式候选目录；中间包不会被收集。
"@
    Set-Content -LiteralPath (Join-Path $packageDirectory 'START_HERE.md') -Value $startHere -Encoding utf8NoBOM

    $metadata = [ordered]@{
        product = 'Vault Delta'
        generatedAt = [DateTimeOffset]::Now.ToString('O')
        branch = $branch
        commit = $commit
        gitDirty = $false
        dotnetSdk = '10.0.101'
        windowsCandidateIncluded = (Test-Path -LiteralPath (Join-Path $releaseDirectory.FullName 'windows'))
        macosCandidateIncluded = (Test-Path -LiteralPath (Join-Path $releaseDirectory.FullName 'macos'))
        nextTask = 'Task 25: macOS Developer ID signing, notarization, Gatekeeper validation, and DMG packaging'
    }
    $metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageDirectory 'HANDOFF_METADATA.json') -Encoding utf8NoBOM

    $checksumPath = Join-Path $packageDirectory 'SHA256SUMS.txt'
    $checksumLines = Get-ChildItem -LiteralPath $packageDirectory -File -Recurse |
        Where-Object { $_.FullName -ne $checksumPath } |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = [System.IO.Path]::GetRelativePath($packageDirectory, $_.FullName).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relativePath"
        }
    Set-Content -LiteralPath $checksumPath -Value $checksumLines -Encoding ascii

    Compress-Archive -LiteralPath $packageDirectory -DestinationPath $archivePath -CompressionLevel Optimal
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$archivePath.sha256" -Value "$archiveHash  $([System.IO.Path]::GetFileName($archivePath))" -Encoding ascii

    Write-Host 'Handoff package created successfully.'
    Write-Host "  Directory: $packageDirectory"
    Write-Host "  Archive:   $archivePath"
    Write-Host "  SHA-256:   $archiveHash"
}
finally {
    if (Test-Path -LiteralPath $temporaryArchive) {
        Remove-Item -LiteralPath $temporaryArchive -Force
    }
}
