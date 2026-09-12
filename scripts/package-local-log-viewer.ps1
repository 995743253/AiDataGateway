param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputPath = "artifacts/AiDataGateway.LocalLogViewer-net48.zip"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src/AiDataGateway.LocalLogViewer/AiDataGateway.LocalLogViewer.csproj"
$buildPath = Join-Path $repositoryRoot ("artifacts/local-log-viewer-build-" + $PID)
$stagingPath = Join-Path $repositoryRoot ("artifacts/local-log-viewer-package-" + $PID)
$packagePath = Join-Path $repositoryRoot $OutputPath
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$resolvedStaging = [IO.Path]::GetFullPath($stagingPath)
$resolvedPackage = [IO.Path]::GetFullPath($packagePath)
$resolvedBuild = [IO.Path]::GetFullPath($buildPath)

if (-not $resolvedStaging.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The staging path must stay inside the repository artifacts directory."
}
if (-not $resolvedPackage.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The output path must stay inside the repository artifacts directory."
}
if (-not $resolvedBuild.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The build path must stay inside the repository artifacts directory."
}

if (Test-Path -LiteralPath $resolvedBuild) { Remove-Item -LiteralPath $resolvedBuild -Recurse -Force }
dotnet build $projectPath -c $Configuration -o $resolvedBuild --no-restore
if ($LASTEXITCODE -ne 0) { throw "WPF build failed with exit code $LASTEXITCODE." }

if (Test-Path -LiteralPath $resolvedStaging) { Remove-Item -LiteralPath $resolvedStaging -Recurse -Force }
New-Item -ItemType Directory -Path $resolvedStaging | Out-Null
Copy-Item -LiteralPath (Join-Path $buildPath "AiDataGateway.LocalLogViewer.exe") -Destination $resolvedStaging
Copy-Item -LiteralPath (Join-Path $buildPath "AiDataGateway.LocalLogViewer.exe.config") -Destination $resolvedStaging
Copy-Item -LiteralPath (Join-Path $buildPath "README.md") -Destination $resolvedStaging
New-Item -ItemType Directory -Path (Join-Path $resolvedStaging "Config") | Out-Null
# Never package the build output Config directory: it may contain a developer's real
# log paths or DPAPI-encrypted database credentials from a local test run.
Copy-Item -LiteralPath (Join-Path $repositoryRoot "src/AiDataGateway.LocalLogViewer/Config/README.txt") -Destination (Join-Path $resolvedStaging "Config/README.txt")

$packageDirectory = Split-Path -Parent $resolvedPackage
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
if (Test-Path -LiteralPath $resolvedPackage) { Remove-Item -LiteralPath $resolvedPackage -Force }
Compress-Archive -Path (Join-Path $resolvedStaging "*") -DestinationPath $resolvedPackage -CompressionLevel Optimal
Remove-Item -LiteralPath $resolvedBuild -Recurse -Force
Write-Host "Created: $resolvedPackage"
