[CmdletBinding()]
param(
    [ValidateRange(1, 1000000)]
    [int]$FileCount = 100000,

    [string]$ExternalVolumePath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repositoryRoot 'tests/VaultDelta.EndToEnd.Tests/VaultDelta.EndToEnd.Tests.csproj'
$previousFileCount = $env:VAULTDELTA_PERF_FILE_COUNT
$previousExternalVolume = $env:VAULTDELTA_EXTERNAL_VOLUME_PATH

try {
    $env:VAULTDELTA_PERF_FILE_COUNT = $FileCount.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    if (-not [string]::IsNullOrWhiteSpace($ExternalVolumePath)) {
        $resolvedExternal = (Resolve-Path -LiteralPath $ExternalVolumePath).Path
        if (-not (Test-Path -LiteralPath $resolvedExternal -PathType Container)) {
            throw "External volume path must be a directory: $resolvedExternal"
        }

        $env:VAULTDELTA_EXTERNAL_VOLUME_PATH = $resolvedExternal
    }

    Write-Host "Vault Delta performance validation"
    Write-Host "  Platform: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)"
    Write-Host "  Synthetic files per snapshot: $FileCount"
    Write-Host "  External volume: $(if ([string]::IsNullOrWhiteSpace($env:VAULTDELTA_EXTERNAL_VOLUME_PATH)) { 'not supplied' } else { $env:VAULTDELTA_EXTERNAL_VOLUME_PATH })"

    dotnet test $testProject `
        --configuration Release `
        --filter 'FullyQualifiedName~LargeVaultPerformanceTests|FullyQualifiedName~ExternalVolumeCapabilityTests' `
        --logger 'console;verbosity=detailed'
    if ($LASTEXITCODE -ne 0) {
        throw "Performance validation failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:VAULTDELTA_PERF_FILE_COUNT = $previousFileCount
    $env:VAULTDELTA_EXTERNAL_VOLUME_PATH = $previousExternalVolume
}
