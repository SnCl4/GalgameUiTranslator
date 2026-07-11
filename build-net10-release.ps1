param(
    [string]$Configuration = "Release",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$localDotnet = Join-Path $projectRoot ".dotnet10\dotnet.exe"
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { "dotnet" }
$testProject = Join-Path $projectRoot "tests\SmokeTests\SmokeTests.Net10.csproj"
$appProject = Join-Path $projectRoot "GalgameUiTranslator.Net10.csproj"
$offlineNugetConfig = Join-Path $projectRoot "NuGet.Config"
$onlineNugetConfig = Join-Path $projectRoot "NuGet.Net10.Config"
$frameworkOutput = Join-Path $projectRoot "publish\GalgameUiTranslator-net10-framework"
$selfContainedOutput = Join-Path $projectRoot "publish\GalgameUiTranslator-net10-win-x64"
$projectXml = [xml](Get-Content -Raw $appProject)
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
$portableOutput = Join-Path $projectRoot "publish\GalgameUiTranslator-v$version-portable"
$portableZip = $portableOutput + ".zip"
$env:DOTNET_CLI_HOME = Join-Path $projectRoot ".dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$sdks = & $dotnet --list-sdks
if (-not ($sdks -match '^10\.')) {
    throw ".NET 10 SDK was not found. Run .\install-dotnet10-local.ps1 first."
}

Write-Host "Restoring the framework-dependent build..."
& $dotnet restore `
    $testProject `
    --configfile $offlineNugetConfig
if ($LASTEXITCODE -ne 0) {
    throw ".NET 10 framework restore failed with exit code $LASTEXITCODE."
}

Write-Host "Running smoke tests..."
& $dotnet run `
    --configuration $Configuration `
    --project $testProject `
    --no-restore
if ($LASTEXITCODE -ne 0) {
    throw ".NET 10 smoke tests failed with exit code $LASTEXITCODE."
}

Write-Host "Publishing the framework-dependent application..."
# AppHostRelativeDotNet is embedded in the executable, so each flavor must
# regenerate the apphost instead of reusing another flavor's cached file.
Get-ChildItem (Join-Path $projectRoot "obj\net10") -Filter "apphost*.exe" -Recurse -File -ErrorAction SilentlyContinue |
    Remove-Item -Force
& $dotnet publish `
    $appProject `
    --configuration $Configuration `
    --output $frameworkOutput `
    --self-contained false `
    --no-restore `
    -p:AppHostRelativeDotNet=..\..\.dotnet10
if ($LASTEXITCODE -ne 0) {
    throw ".NET 10 framework publish failed with exit code $LASTEXITCODE."
}

Write-Host "Framework-dependent build completed: $(Join-Path $frameworkOutput 'GalgameUiTranslator.exe')"

Write-Host "Creating the portable package with a private desktop runtime..."
if (Test-Path $portableOutput) {
    $publishRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "publish"))
    $resolvedPortable = [System.IO.Path]::GetFullPath($portableOutput)
    if (-not $resolvedPortable.StartsWith($publishRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Portable output path is outside the publish directory."
    }
    Remove-Item -LiteralPath $resolvedPortable -Recurse -Force
}

Get-ChildItem (Join-Path $projectRoot "obj\net10") -Filter "apphost*.exe" -Recurse -File -ErrorAction SilentlyContinue |
    Remove-Item -Force

& $dotnet publish `
    $appProject `
    --configuration $Configuration `
    --output $portableOutput `
    --self-contained false `
    --no-restore `
    -p:AppHostRelativeDotNet=.runtime
if ($LASTEXITCODE -ne 0) {
    throw ".NET 10 portable application publish failed with exit code $LASTEXITCODE."
}

$runtimeRoot = Join-Path $portableOutput ".runtime"
$coreRuntime = Get-ChildItem (Join-Path $projectRoot ".dotnet10\shared\Microsoft.NETCore.App") -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$desktopRuntime = Get-ChildItem (Join-Path $projectRoot ".dotnet10\shared\Microsoft.WindowsDesktop.App") -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$hostFxr = Get-ChildItem (Join-Path $projectRoot ".dotnet10\host\fxr") -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if ($null -eq $coreRuntime -or $null -eq $desktopRuntime -or $null -eq $hostFxr) {
    throw "The local .NET desktop runtime is incomplete. Run .\install-dotnet10-local.ps1 again."
}

New-Item -ItemType Directory -Force (Join-Path $runtimeRoot "shared\Microsoft.NETCore.App") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $runtimeRoot "shared\Microsoft.WindowsDesktop.App") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $runtimeRoot "host\fxr") | Out-Null
Copy-Item -LiteralPath $coreRuntime.FullName -Destination (Join-Path $runtimeRoot "shared\Microsoft.NETCore.App") -Recurse
Copy-Item -LiteralPath $desktopRuntime.FullName -Destination (Join-Path $runtimeRoot "shared\Microsoft.WindowsDesktop.App") -Recurse
Copy-Item -LiteralPath $hostFxr.FullName -Destination (Join-Path $runtimeRoot "host\fxr") -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot ".dotnet10\dotnet.exe") -Destination $runtimeRoot
Copy-Item -LiteralPath (Join-Path $projectRoot ".dotnet10\LICENSE.txt") -Destination $runtimeRoot
Copy-Item -LiteralPath (Join-Path $projectRoot ".dotnet10\ThirdPartyNotices.txt") -Destination $runtimeRoot
Copy-Item -LiteralPath (Join-Path $projectRoot "PORTABLE_README.txt") -Destination $portableOutput

if (Test-Path $portableZip) { Remove-Item -LiteralPath $portableZip -Force }
Compress-Archive -Path (Join-Path $portableOutput "*") -DestinationPath $portableZip -CompressionLevel Optimal
Write-Host "Portable folder completed: $(Join-Path $portableOutput 'GalgameUiTranslator.exe')"
Write-Host "Portable ZIP completed: $portableZip"
if (-not $SelfContained) {
    exit 0
}

Write-Host "Restoring packages for the self-contained single-file build..."
& $dotnet restore `
    $appProject `
    --runtime win-x64 `
    --configfile $onlineNugetConfig `
    -p:SelfContained=true `
    -p:PublishSingleFile=true

if ($LASTEXITCODE -ne 0) {
    throw "The self-contained runtime packages could not be downloaded from api.nuget.org."
}

& $dotnet publish `
    $appProject `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $selfContainedOutput `
    --no-restore `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) {
    throw "The self-contained publish failed with exit code $LASTEXITCODE."
}

Write-Host "Self-contained build completed: $(Join-Path $selfContainedOutput 'GalgameUiTranslator.exe')"
