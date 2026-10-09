<#
.SYNOPSIS
Packs a Release build of the add-in into the files a GitHub release carries.

.DESCRIPTION
Writes to tools/dist/out/ (git-ignored):
  Genizah-<VERSION>.zip     the add-in's bin\Release, without symbols or import libraries.
                            model/ holds vocab.txt, thresholds.json and manifest.json, which
                            names the graph by sha256, but not the graph: build with
                            -p:IncludeModelGraph=false, since the signed .dll.manifest lists
                            every file VSTO must find.
  model-manifest.json       a copy of model/manifest.json, so an update check can tell whether
                            the graph changed without downloading the zip.
The graph itself is its own release (publish_model.ps1), so an add-in update whose manifest
names the same sha256 needs no new model download.

.EXAMPLE
powershell -File tools/dist/package.ps1
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'out')
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$bin = Join-Path $root 'Genizah\bin\Release'
if (-not (Test-Path (Join-Path $bin 'Genizah.vsto'))) { throw "no Release build in $bin" }

$built = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $bin 'Genizah.dll')).Version.ToString()
if ($built -ne "$version.0") { throw "bin\Release\Genizah.dll is $built, but VERSION is ${version}: rebuild" }

if (Select-String -Path (Join-Path $bin 'Genizah.dll.manifest') -Pattern '\.onnx"' -Quiet) {
    throw "the signed Genizah.dll.manifest lists the model graph: rebuild with -p:IncludeModelGraph=false"
}

$stage = Join-Path $OutDir 'stage'
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null
Copy-Item (Join-Path $bin '*') $stage -Recurse
Get-ChildItem $stage -Recurse -Include *.pdb, *.lib, *.onnx | Remove-Item

$zip = Join-Path $OutDir "Genizah-$version.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Copy-Item (Join-Path $root 'model\manifest.json') (Join-Path $OutDir 'model-manifest.json')
Remove-Item $stage -Recurse -Force
Get-ChildItem $OutDir | ForEach-Object { Write-Host ("{0}  {1:N0} bytes" -f $_.Name, $_.Length) }
