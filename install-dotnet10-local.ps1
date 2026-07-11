param(
    [string]$Channel = "10.0"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$installer = Join-Path $projectRoot ".dotnet-install.ps1"
$installDirectory = Join-Path $projectRoot ".dotnet10"

if (-not (Test-Path $installer)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest `
        -UseBasicParsing `
        -Uri "https://dot.net/v1/dotnet-install.ps1" `
        -OutFile $installer
}

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer `
    -Channel $Channel `
    -Architecture x64 `
    -InstallDir $installDirectory `
    -NoPath
if ($LASTEXITCODE -ne 0) {
    throw ".NET 10 SDK local installation failed with exit code $LASTEXITCODE."
}

Write-Host "SDK installed at: $installDirectory"
