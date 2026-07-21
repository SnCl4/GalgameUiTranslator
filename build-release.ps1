param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $projectRoot "build-net10-release.ps1") -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Release build failed with exit code $LASTEXITCODE."
}
