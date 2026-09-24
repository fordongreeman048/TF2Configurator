<#
.SYNOPSIS
    Refreshes the bundled mastercomfig data files from the upstream repository.

.DESCRIPTION
    Downloads modules.json, preset_modules.json and module_values.json from
    mastercomfig's develop branch and writes them into data\ (the working copies)
    and src\TF2Configurator\Assets\ (the embedded resources). Each file is
    validated as JSON before anything is written, so a partial download can never
    land in the tree.

    Run this before a release whenever mastercomfig has changed its data. The app
    itself can also refresh at runtime, but that only updates the per-user cache;
    the bundled snapshots are what ship.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\update-catalog.ps1
#>

$ErrorActionPreference = 'Stop'

$base = 'https://raw.githubusercontent.com/mastercomfig/mastercomfig/develop/data'
$files = @('modules.json', 'preset_modules.json', 'module_values.json')

$root = Split-Path -Parent $PSScriptRoot
$dataDir = Join-Path $root 'data'
$assetsDir = Join-Path $root 'src\TF2Configurator\Assets'

$downloaded = @{}
foreach ($file in $files) {
    $url = "$base/$file"
    Write-Host "Downloading $url"
    $text = (Invoke-WebRequest -Uri $url -UseBasicParsing).Content

    # Validate before writing: a truncated or wrong payload must never replace the bundle.
    try {
        $null = $text | ConvertFrom-Json
    } catch {
        throw "Refusing to write $file : downloaded content is not valid JSON ($($_.Exception.Message))"
    }

    $downloaded[$file] = $text
}

foreach ($file in $files) {
    $destinations = @(
        (Join-Path $dataDir $file),
        (Join-Path $assetsDir $file)
    )
    foreach ($dest in $destinations) {
        [System.IO.File]::WriteAllText($dest, $downloaded[$file], [System.Text.UTF8Encoding]::new($false))
        Write-Host "Wrote $dest"
    }
}

# Sanity summary so a wrong update is obvious in the terminal.
$catalog = $downloaded['modules.json'] | ConvertFrom-Json
$modules = 0
foreach ($prop in $catalog.PSObject.Properties) { $modules += $prop.Value.modules.Count }
$presets = $downloaded['preset_modules.json'] | ConvertFrom-Json
$presetCount = @($presets.PSObject.Properties).Count

Write-Host ''
Write-Host "Updated bundle: $modules modules, $presetCount preset mappings."
Write-Host 'Rebuild the app for the new snapshots to take effect.'
