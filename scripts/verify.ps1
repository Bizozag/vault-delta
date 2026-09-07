[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'VaultDelta.sln'

Push-Location $repositoryRoot
try {
    dotnet format $solutionPath --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet format failed with exit code $LASTEXITCODE."
    }

    dotnet build $solutionPath --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }

    dotnet test $solutionPath --configuration $Configuration --no-build --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
