param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputPath = "artifacts/extensions/project-issue-tracker-1.2.0.zip"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "samples/ProjectIssueExtension/ProjectIssueExtension.csproj"
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$publishPath = [IO.Path]::GetFullPath((Join-Path $artifactsRoot ("project-issue-package-" + $PID)))
$packagePath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
if (-not $publishPath.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Publish path must stay inside artifacts." }
if (-not $packagePath.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Package path must stay inside artifacts." }

dotnet publish $projectPath -c $Configuration -o $publishPath --no-self-contained --no-restore
if ($LASTEXITCODE -ne 0) { throw "Extension build failed with exit code $LASTEXITCODE." }
foreach ($name in @("AiDataGateway.Extensions.Abstractions.dll", "AiDataGateway.Extensions.Abstractions.xml", "AiDataGateway.Extensions.Abstractions.pdb")) {
    $file = Join-Path $publishPath $name
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
}
New-Item -ItemType Directory -Path (Split-Path -Parent $packagePath) -Force | Out-Null
if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
Compress-Archive -Path (Join-Path $publishPath "*") -DestinationPath $packagePath -CompressionLevel Optimal
Write-Host "Extension package: $packagePath"
