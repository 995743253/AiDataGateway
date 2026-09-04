param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,
    [string]$OutputDirectory = "artifacts/release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$applicationDirectory = Join-Path $releaseRoot "AiDataGateway-v$Version-win-x64"
$agentDirectory = Join-Path $releaseRoot "AiDataGateway-MonitorAgent-v$Version-win-x64"
$installerPublishDirectory = Join-Path $releaseRoot "installer-publish"
$payloadPath = Join-Path $repositoryRoot "src/AiDataGateway.Installer/Payload/AiDataGateway-payload.zip"

if ($releaseRoot -notlike "$repositoryRoot\artifacts\*") {
    throw "OutputDirectory must be located under the repository artifacts directory."
}

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseRoot | Out-Null

Push-Location (Join-Path $repositoryRoot "src/AiDataGateway.Web")
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm build failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

dotnet restore (Join-Path $repositoryRoot "AiDataGateway.sln")
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
dotnet test (Join-Path $repositoryRoot "AiDataGateway.sln") -c Release --no-restore -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }
dotnet publish (Join-Path $repositoryRoot "src/AiDataGateway.Desktop/AiDataGateway.Desktop.csproj") `
    -c Release -r win-x64 --self-contained true -p:Version=$Version `
    -p:DebugType=None -p:DebugSymbols=false -o $applicationDirectory
if ($LASTEXITCODE -ne 0) { throw "desktop publish failed with exit code $LASTEXITCODE." }
dotnet publish (Join-Path $repositoryRoot "src/AiDataGateway.MonitorAgent/AiDataGateway.MonitorAgent.csproj") `
    -c Release -r win-x64 --self-contained true -p:Version=$Version `
    -p:DebugType=None -p:DebugSymbols=false -o $agentDirectory
if ($LASTEXITCODE -ne 0) { throw "monitor agent publish failed with exit code $LASTEXITCODE." }

$optionalRuntime = Join-Path $repositoryRoot "WebView2Runtime"
if (Test-Path -LiteralPath (Join-Path $optionalRuntime "msedgewebview2.exe")) {
    Copy-Item -LiteralPath $optionalRuntime -Destination (Join-Path $applicationDirectory "WebView2Runtime") -Recurse
}

Copy-Item -LiteralPath $agentDirectory -Destination (Join-Path $applicationDirectory "MonitorAgent") -Recurse
$applicationZip = Join-Path $releaseRoot "AiDataGateway-v$Version-win-x64.zip"
$agentZip = Join-Path $releaseRoot "AiDataGateway-MonitorAgent-v$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $applicationDirectory "*") -DestinationPath $applicationZip -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $agentDirectory "*") -DestinationPath $agentZip -CompressionLevel Optimal

# 定制化模块示例：样例源码与打包好的扩展包随安装包分发，供企业扩展开发者参考
$samplesSource = Join-Path $repositoryRoot "samples/LocalMonitorReportExtension"
$samplesRelease = Join-Path $releaseRoot "samples/LocalMonitorReportExtension"
if (Test-Path -LiteralPath $samplesSource) {
    Copy-Item -LiteralPath $samplesSource -Destination $samplesRelease -Recurse -Force
    foreach ($buildFolder in @("bin", "obj")) {
        $samplesBuildPath = Join-Path $samplesRelease $buildFolder
        if (Test-Path -LiteralPath $samplesBuildPath) { Remove-Item -LiteralPath $samplesBuildPath -Recurse -Force }
    }
}

$samplePackageScript = Join-Path $repositoryRoot "scripts/package-local-monitor-report-extension.ps1"
$samplePackagePath = Join-Path $releaseRoot "local-monitor-report-1.0.0.zip"
if (Test-Path -LiteralPath $samplePackageScript) {
    & $samplePackageScript -OutputPath "artifacts/release/local-monitor-report-1.0.0.zip"
    if ($LASTEXITCODE -ne 0) { throw "sample extension packaging failed with exit code $LASTEXITCODE." }
    $sampleHash = (Get-FileHash -LiteralPath $samplePackagePath -Algorithm SHA256).Hash
    Set-Content -LiteralPath "$samplePackagePath.sha256" -Value "$sampleHash  $([System.IO.Path]::GetFileName($samplePackagePath))" -Encoding ascii
}

New-Item -ItemType Directory -Path (Split-Path $payloadPath) -Force | Out-Null
Copy-Item -LiteralPath $applicationZip -Destination $payloadPath -Force
try {
    dotnet publish (Join-Path $repositoryRoot "src/AiDataGateway.Installer/AiDataGateway.Installer.csproj") `
        -c Release -r win-x64 --self-contained true -p:Version=$Version `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
        -o $installerPublishDirectory
    if ($LASTEXITCODE -ne 0) { throw "installer publish failed with exit code $LASTEXITCODE." }
}
finally {
    if (Test-Path -LiteralPath $payloadPath) { Remove-Item -LiteralPath $payloadPath -Force }
}

$installer = Join-Path $releaseRoot "AiDataGateway-Setup-v$Version-win-x64.exe"
Copy-Item -LiteralPath (Join-Path $installerPublishDirectory "AiDataGateway.Setup.exe") -Destination $installer -Force

foreach ($file in @($applicationZip, $agentZip, $installer)) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    Set-Content -LiteralPath "$file.sha256" -Value "$hash  $([System.IO.Path]::GetFileName($file))" -Encoding ascii
}

Remove-Item -LiteralPath $applicationDirectory -Recurse -Force
Remove-Item -LiteralPath $agentDirectory -Recurse -Force
Remove-Item -LiteralPath $installerPublishDirectory -Recurse -Force

Get-ChildItem -LiteralPath $releaseRoot -File | Select-Object Name, Length, LastWriteTime
