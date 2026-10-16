<#
.SYNOPSIS
Packs a Release build of the add-in into what a release publishes.

.DESCRIPTION
Reads Genizah\bin\Release (msbuild -t:Build -p:Configuration=Release, with the graph fetched into
model/ first) and writes to tools/dist/out/ (git-ignored):
  assets/    the GitHub release's assets: Genizah.vsto, Genizah.dll.manifest, and every file the
             .dll.manifest lists, under its flat name (model\vocab.txt becomes model.vocab.txt).
             The updater downloads files by these names.
  version/   the same files under their real paths: a version's folder, which the installer packs.
Checks that the build is VERSION, that every listed file has the size and sha256 its signed
manifest gives, and that the flat names are ones GitHub keeps as they are. See docs/RELEASING.md.

.EXAMPLE
powershell -File tools/dist/package.ps1
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'out')
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$build = Join-Path $root 'Genizah\bin\Release'
if (-not (Test-Path (Join-Path $build 'Genizah.vsto'))) {
    throw "no Release build in $build (msbuild Genizah/Genizah.csproj -restore -t:Build -p:Configuration=Release)"
}

[xml]$vsto = Get-Content (Join-Path $build 'Genizah.vsto') -Raw
$built = $vsto.assembly.assemblyIdentity.version
if ($built -ne "$version.0") { throw "bin\Release\Genizah.vsto is $built, but VERSION is ${version}: build again" }
$manifestName = $vsto.assembly.dependency.dependentAssembly.codebase
if ($manifestName -ne 'Genizah.dll.manifest') { throw "Genizah.vsto points at '$manifestName', not Genizah.dll.manifest" }

# The files the signed .dll.manifest lists: assemblies with a codebase, and plain files.
[xml]$app = Get-Content (Join-Path $build $manifestName) -Raw
$files = @($app.assembly.dependency.dependentAssembly | Where-Object { $_.codebase } |
           ForEach-Object { [pscustomobject]@{ Path = $_.codebase; Size = [long]$_.size; Sha = $_.hash.DigestValue } }) +
         @($app.assembly.file |
           ForEach-Object { [pscustomobject]@{ Path = $_.name; Size = [long]$_.size; Sha = $_.hash.DigestValue } })
if (-not ($files | Where-Object { $_.Path -like 'model\*.onnx' })) {
    throw "the build has no model graph: run tools/dist/fetch_model.ps1 before building"
}

# GitHub renames asset names with other characters, and with a leading or trailing period
# (Genizah.Core's Manifests.cs refuses the same paths).
$names = @{}
foreach ($name in @('Genizah.vsto', 'Genizah.dll.manifest') + ($files | ForEach-Object { $_.Path.Replace('\', '.') })) {
    if ($name -notmatch '^[A-Za-z0-9_-][A-Za-z0-9._-]*[A-Za-z0-9_-]$' -or $name -match '\.\.') {
        throw "'$name' isn't an asset name GitHub keeps as it is"
    }
    if ($names.ContainsKey($name)) { throw "two files are both asset '$name'" }   # keys ignore case
    $names[$name] = $true
}

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
$assets = Join-Path $OutDir 'assets'
$folder = Join-Path $OutDir 'version'
New-Item -ItemType Directory $assets, $folder | Out-Null
$sha256 = [Security.Cryptography.SHA256]::Create()
foreach ($f in $files) {
    $source = Join-Path $build $f.Path
    $item = Get-Item $source
    if ($item.Length -ne $f.Size) { throw "$($f.Path) is $($item.Length) bytes, but the manifest says $($f.Size)" }
    $stream = [IO.File]::OpenRead($source)
    try { $got = [Convert]::ToBase64String($sha256.ComputeHash($stream)) } finally { $stream.Dispose() }
    if ($got -ne $f.Sha) { throw "$($f.Path) doesn't have the sha256 the manifest gives" }
    Copy-Item $source (Join-Path $assets $f.Path.Replace('\', '.'))
    $to = Join-Path $folder $f.Path
    New-Item -ItemType Directory (Split-Path $to) -Force | Out-Null
    Copy-Item $source $to
}
foreach ($m in 'Genizah.vsto', 'Genizah.dll.manifest') {
    Copy-Item (Join-Path $build $m) $assets
    Copy-Item (Join-Path $build $m) $folder
}

Get-ChildItem $assets | Sort-Object Name | ForEach-Object { Write-Host ("{0}  {1:N0} bytes" -f $_.Name, $_.Length) }
