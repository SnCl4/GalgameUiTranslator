param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:DOTNET_CLI_HOME = Join-Path $projectRoot ".dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

dotnet publish `
    (Join-Path $projectRoot "GalgameUiTranslator.csproj") `
    --configuration $Configuration `
    --output (Join-Path $projectRoot "publish\GalgameUiTranslator") `
    --configfile (Join-Path $projectRoot "NuGet.Config")
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

Write-Host "Build completed: $(Join-Path $projectRoot 'publish\GalgameUiTranslator\GalgameUiTranslator.exe')"
