param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [string[]]$AdditionalAssemblyNames = @(),
    [string]$MappingDirectory = "artifacts/obfuscation-maps/extensions"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$sourcePackage = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $PackagePath))
$targetPackage = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
$mapPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $MappingDirectory))
$protectionScript = Join-Path $repositoryRoot "build/Protect-DotNetArtifacts.ps1"

if (-not (Test-Path -LiteralPath $sourcePackage -PathType Leaf)) {
    throw "Extension package does not exist: $sourcePackage"
}
if (-not $sourcePackage.EndsWith(".zip", [System.StringComparison]::OrdinalIgnoreCase) -or
    -not $targetPackage.EndsWith(".zip", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "PackagePath and OutputPath must be ZIP files."
}
if ([string]::Equals($sourcePackage, $targetPackage, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must be different from PackagePath so the original package is preserved."
}

$workingPath = Join-Path ([System.IO.Path]::GetTempPath()) ("AiDataGateway-extension-protect-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $workingPath -Force | Out-Null
try {
    Expand-Archive -LiteralPath $sourcePackage -DestinationPath $workingPath
    $manifestPath = Join-Path $workingPath "gateway-extension.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "The extension package does not contain gateway-extension.json."
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $entryAssembly = [string]$manifest.entryAssembly
    if ([string]::IsNullOrWhiteSpace($entryAssembly) -or
        [System.IO.Path]::GetFileName($entryAssembly) -ne $entryAssembly) {
        throw "The protection helper currently requires entryAssembly to be a DLL in the ZIP root."
    }

    $assemblyNames = @($entryAssembly) + $AdditionalAssemblyNames |
        Where-Object { $_ -and $_ -ne "AiDataGateway.Extensions.Abstractions.dll" } |
        Select-Object -Unique

    # The contract DLL is intentionally excluded from extension ZIPs, but the
    # obfuscator still needs it while resolving the entry assembly references.
    $contractAssembly = Join-Path $repositoryRoot "src/AiDataGateway.Extensions.Abstractions/bin/Release/net10.0/AiDataGateway.Extensions.Abstractions.dll"
    if (-not (Test-Path -LiteralPath $contractAssembly -PathType Leaf)) {
        dotnet build (Join-Path $repositoryRoot "src/AiDataGateway.Extensions.Abstractions/AiDataGateway.Extensions.Abstractions.csproj") `
            -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Building the extension contract failed with exit code $LASTEXITCODE." }
    }
    $temporaryContract = Join-Path $workingPath "AiDataGateway.Extensions.Abstractions.dll"
    Copy-Item -LiteralPath $contractAssembly -Destination $temporaryContract -Force

    & $protectionScript -InputDirectory $workingPath -AssemblyNames $assemblyNames `
        -MappingDirectory $mapPath -KeepPublicApi
    if ($LASTEXITCODE -ne 0) { throw "Extension protection failed with exit code $LASTEXITCODE." }
    Remove-Item -LiteralPath $temporaryContract -Force
    Get-ChildItem -LiteralPath $workingPath -Recurse -File -Include *.pdb |
        Remove-Item -Force

    New-Item -ItemType Directory -Path (Split-Path -Parent $targetPackage) -Force | Out-Null
    if (Test-Path -LiteralPath $targetPackage) { Remove-Item -LiteralPath $targetPackage -Force }
    Compress-Archive -Path (Join-Path $workingPath "*") -DestinationPath $targetPackage -CompressionLevel Optimal
    Write-Host "Protected extension package: $targetPackage"
    Write-Host "Keep the private mapping directory secure: $mapPath"
}
finally {
    if (Test-Path -LiteralPath $workingPath) { Remove-Item -LiteralPath $workingPath -Recurse -Force }
}
