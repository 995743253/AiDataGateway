param(
    [Parameter(Mandatory = $true)]
    [string]$InputDirectory,
    [Parameter(Mandatory = $true)]
    [string[]]$AssemblyNames,
    [Parameter(Mandatory = $true)]
    [string]$MappingDirectory,
    [switch]$KeepPublicApi
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$inputPath = [System.IO.Path]::GetFullPath($InputDirectory)
$mapPath = [System.IO.Path]::GetFullPath($MappingDirectory)

if (-not (Test-Path -LiteralPath $inputPath -PathType Container)) {
    throw "Obfuscation input directory does not exist: $inputPath"
}

$modules = @()
foreach ($assemblyName in $AssemblyNames) {
    if ([System.IO.Path]::GetFileName($assemblyName) -ne $assemblyName -or
        -not $assemblyName.EndsWith(".dll", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "AssemblyNames must contain DLL file names only: $assemblyName"
    }

    $assemblyPath = Join-Path $inputPath $assemblyName
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "Assembly to protect was not found: $assemblyPath"
    }
    $modules += $assemblyPath
}

New-Item -ItemType Directory -Path $mapPath -Force | Out-Null
$workingPath = Join-Path $mapPath ("work-" + [Guid]::NewGuid().ToString("N"))
$outputPath = Join-Path $workingPath "output"
$configurationPath = Join-Path $workingPath "obfuscar.xml"
$mappingFile = Join-Path $mapPath "mapping.xml"
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

try {
    $document = [System.Xml.XmlDocument]::new()
    $root = $document.CreateElement("Obfuscator")
    [void]$document.AppendChild($root)

    function Add-Variable([string]$name, [string]$value) {
        $element = $document.CreateElement("Var")
        $element.SetAttribute("name", $name)
        $element.SetAttribute("value", $value)
        [void]$root.AppendChild($element)
    }

    Add-Variable "InPath" $inputPath
    Add-Variable "OutPath" $outputPath
    Add-Variable "LogFile" $mappingFile
    Add-Variable "XmlMapping" "true"
    Add-Variable "KeepPublicApi" ($KeepPublicApi.IsPresent.ToString().ToLowerInvariant())
    Add-Variable "HidePrivateApi" "true"
    Add-Variable "RenameProperties" "false"
    Add-Variable "RenameEvents" "false"
    Add-Variable "RenameFields" "false"
    Add-Variable "ReuseNames" "false"
    Add-Variable "UseUnicodeNames" "true"
    # HideStrings must stay off: with it enabled, Obfuscar's string encryption
    # corrupts compiler-generated lambda classes (<>c) in .NET 10 assemblies —
    # type load fails with "because the format is invalid" at runtime
    # (reproduced on DataSourceDefinition.SetBlockedTables, missed by the
    # smoke test until it started exercising data source creation).
    Add-Variable "HideStrings" "false"
    Add-Variable "OptimizeMethods" "true"
    Add-Variable "SuppressIldasm" "true"
    Add-Variable "AnalyzeXaml" "true"
    Add-Variable "SkipGenerated" "true"
    Add-Variable "SkipSpecialName" "true"

    foreach ($module in $modules) {
        $element = $document.CreateElement("Module")
        $element.SetAttribute("file", $module)
        if ($KeepPublicApi.IsPresent) {
            # Interface implementations on effectively internal types are not always
            # recognized by every obfuscator/runtime combination. Preserve method
            # names to keep BCL and plugin contracts valid while still renaming types,
            # parameters and hiding strings.
            $skipMethods = $document.CreateElement("SkipMethod")
            $skipMethods.SetAttribute("type", "*")
            $skipMethods.SetAttribute("name", "*")
            [void]$element.AppendChild($skipMethods)
        }
        [void]$root.AppendChild($element)
    }

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Indent = $true
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $writer = [System.Xml.XmlWriter]::Create($configurationPath, $settings)
    try { $document.Save($writer) } finally { $writer.Dispose() }

    Push-Location $repositoryRoot
    try {
        dotnet tool run obfuscar.console -- $configurationPath
        if ($LASTEXITCODE -ne 0) { throw "Obfuscar failed with exit code $LASTEXITCODE." }
    }
    finally {
        Pop-Location
    }

    foreach ($assemblyName in $AssemblyNames) {
        $protectedAssembly = Join-Path $outputPath $assemblyName
        if (-not (Test-Path -LiteralPath $protectedAssembly -PathType Leaf)) {
            throw "Protected assembly was not generated: $protectedAssembly"
        }
        Copy-Item -LiteralPath $protectedAssembly -Destination (Join-Path $inputPath $assemblyName) -Force
    }

    if (-not (Test-Path -LiteralPath $mappingFile -PathType Leaf) -or
        (Get-Item -LiteralPath $mappingFile).Length -eq 0) {
        throw "Obfuscation mapping was not generated."
    }

    Write-Host "Protected $($AssemblyNames.Count) assemblies in $inputPath"
    Write-Host "Private mapping file: $mappingFile"
}
finally {
    if (Test-Path -LiteralPath $workingPath) {
        Remove-Item -LiteralPath $workingPath -Recurse -Force
    }
}
