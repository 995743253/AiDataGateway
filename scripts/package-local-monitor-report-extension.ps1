param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "artifacts/extensions/local-monitor-report-1.0.0.zip"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "samples/LocalMonitorReportExtension/LocalMonitorReportExtension.csproj"
$publishPath = Join-Path $repositoryRoot "artifacts/extensions/local-monitor-report-package"
$packagePath = Join-Path $repositoryRoot $OutputPath

if (Test-Path -LiteralPath $publishPath) { Remove-Item -LiteralPath $publishPath -Recurse -Force }
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
dotnet publish $projectPath -c $Configuration -o $publishPath --no-self-contained --no-restore

$abstractionsDll = Join-Path $publishPath "AiDataGateway.Extensions.Abstractions.dll"
$abstractionsXml = Join-Path $publishPath "AiDataGateway.Extensions.Abstractions.xml"
$abstractionsPdb = Join-Path $publishPath "AiDataGateway.Extensions.Abstractions.pdb"
if (Test-Path -LiteralPath $abstractionsDll) { Remove-Item -LiteralPath $abstractionsDll -Force }
if (Test-Path -LiteralPath $abstractionsXml) { Remove-Item -LiteralPath $abstractionsXml -Force }
if (Test-Path -LiteralPath $abstractionsPdb) { Remove-Item -LiteralPath $abstractionsPdb -Force }

New-Item -ItemType Directory -Path (Split-Path -Parent $packagePath) -Force | Out-Null
if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
Compress-Archive -Path (Join-Path $publishPath "*") -DestinationPath $packagePath -CompressionLevel Optimal
Write-Host "Extension package: $packagePath"
