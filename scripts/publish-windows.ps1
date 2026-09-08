[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputRoot,

    [switch]$SkipVerify,

    [switch]$SkipSmokeTest
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src/VaultDelta.Desktop/VaultDelta.Desktop.csproj'
$verifyScript = Join-Path $PSScriptRoot 'verify.ps1'
$packageReadme = Join-Path $repositoryRoot 'packaging/windows/README.txt'
$resolvedOutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $repositoryRoot 'artifacts/releases/windows'
}
else {
    [System.IO.Path]::GetFullPath($OutputRoot, $repositoryRoot)
}
$packageName = "VaultDelta-$Version-win-x64"
$packageDirectory = Join-Path $resolvedOutputRoot $packageName
$archivePath = Join-Path $resolvedOutputRoot "$packageName.zip"
$checksumPath = Join-Path $resolvedOutputRoot 'SHA256SUMS.txt'

if (-not $IsWindows) {
    throw 'Windows packaging must run on Windows so the published executable can be smoke-tested.'
}

if (Test-Path -LiteralPath $packageDirectory) {
    throw "Release directory already exists: $packageDirectory"
}

if (Test-Path -LiteralPath $archivePath) {
    throw "Release archive already exists: $archivePath"
}

if (-not $SkipVerify) {
    & $verifyScript -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Repository verification failed with exit code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) "vaultdelta-windows-package-$([Guid]::NewGuid().ToString('N'))"
$publishDirectory = Join-Path $stagingRoot $packageName

try {
    dotnet publish $projectPath `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        --output $publishDirectory `
        -p:Version=$Version `
        -p:PublishSingleFile=false `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $requiredFiles = @(
        'VaultDelta.exe',
        'VaultDelta.dll',
        'VaultDelta.deps.json',
        'VaultDelta.runtimeconfig.json',
        'coreclr.dll',
        'hostfxr.dll',
        'Avalonia.Base.dll',
        'VaultDelta.Application.dll',
        'VaultDelta.Domain.dll',
        'VaultDelta.Infrastructure.dll'
    )
    foreach ($requiredFile in $requiredFiles) {
        $requiredPath = Join-Path $publishDirectory $requiredFile
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Published package is missing required file: $requiredFile"
        }
    }

    Copy-Item -LiteralPath $packageReadme -Destination (Join-Path $publishDirectory 'README.txt')
    $gitCommit = (git -C $repositoryRoot rev-parse HEAD).Trim()
    $gitDirty = -not [string]::IsNullOrWhiteSpace((git -C $repositoryRoot status --porcelain))
    $executable = Join-Path $publishDirectory 'VaultDelta.exe'
    $signature = Get-AuthenticodeSignature -LiteralPath $executable
    $releaseInfo = [ordered]@{
        product = 'Vault Delta'
        version = $Version
        runtimeIdentifier = 'win-x64'
        selfContained = $true
        framework = 'net10.0'
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        gitCommit = $gitCommit
        gitDirty = $gitDirty
        signatureStatus = $signature.Status.ToString()
    }
    $releaseInfo | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publishDirectory 'release.json') -Encoding utf8NoBOM

    if (-not $SkipSmokeTest) {
        $process = Start-Process -FilePath $executable -WorkingDirectory $publishDirectory -WindowStyle Hidden -PassThru
        try {
            Start-Sleep -Seconds 3
            $process.Refresh()
            if ($process.HasExited) {
                throw "Published application exited during smoke test with code $($process.ExitCode)."
            }
        }
        finally {
            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id -Force
                $process.WaitForExit()
            }
        }
    }

    Move-Item -LiteralPath $publishDirectory -Destination $packageDirectory
    Compress-Archive -LiteralPath $packageDirectory -DestinationPath $archivePath -CompressionLevel Optimal
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$archiveHash  $([System.IO.Path]::GetFileName($archivePath))" | Set-Content -LiteralPath $checksumPath -Encoding ascii

    $archive = Get-Item -LiteralPath $archivePath
    Write-Host 'Windows package created successfully.'
    Write-Host "  Directory: $packageDirectory"
    Write-Host "  Archive:   $archivePath"
    Write-Host "  Size:      $($archive.Length) bytes"
    Write-Host "  SHA-256:   $archiveHash"
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
