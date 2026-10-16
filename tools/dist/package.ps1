<#
.SYNOPSIS
Packs a Release ClickOnce publish of the add-in into what a release publishes.

.DESCRIPTION
Reads Genizah\bin\Release\app.publish (msbuild -t:Publish) and writes to tools/dist/out/
(git-ignored):
  site/          the GitHub Pages site: Genizah.vsto, Application Files/Genizah_<version>/
                 and release.json. Only this version's files, since installs and updates
                 only ever fetch the latest.
  release.json   the version and the model graph it needs (file name and sha256, from
                 model/manifest.json). The add-in reads it from Pages to decide whether to
                 update, and which graph to download first.
The graph itself is never in the deployment: publish with -p:IncludeModelGraph=false, since
the signed .dll.manifest lists every file VSTO installs. See docs/RELEASING.md.

.EXAMPLE
powershell -File tools/dist/package.ps1
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'out')
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$publish = Join-Path $root 'Genizah\bin\Release\app.publish'
$appDir = Join-Path $publish ("Application Files\Genizah_" + "$version.0".Replace('.', '_'))
if (-not (Test-Path (Join-Path $appDir 'Genizah.dll.manifest'))) {
    throw "no ClickOnce publish of $version in $publish (msbuild -t:Publish -p:Configuration=Release -p:IncludeModelGraph=false)"
}

[xml]$vsto = Get-Content (Join-Path $publish 'Genizah.vsto') -Raw
$published = $vsto.assembly.assemblyIdentity.version
if ($published -ne "$version.0") { throw "app.publish\Genizah.vsto is $published, but VERSION is ${version}: publish again" }

if (Select-String -Path (Join-Path $appDir 'Genizah.dll.manifest') -Pattern '\.onnx"' -Quiet) {
    throw "the signed Genizah.dll.manifest lists the model graph: publish with -p:IncludeModelGraph=false"
}

$manifest = Get-Content (Join-Path $root 'model\manifest.json') -Raw | ConvertFrom-Json
$release = [ordered]@{
    version      = $version
    model_file   = $manifest.model_file
    model_sha256 = $manifest.model_sha256
} | ConvertTo-Json

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
$site = Join-Path $OutDir 'site'
New-Item -ItemType Directory (Join-Path $site 'Application Files') | Out-Null
Copy-Item (Join-Path $publish 'Genizah.vsto') $site
Copy-Item $appDir (Join-Path $site 'Application Files') -Recurse
# UTF-8 without a BOM, so any JSON reader takes it.
$utf8 = New-Object Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $site 'release.json'), $release, $utf8)
[IO.File]::WriteAllText((Join-Path $OutDir 'release.json'), $release, $utf8)

Get-ChildItem $site -Recurse -File | ForEach-Object {
    Write-Host ("{0}  {1:N0} bytes" -f $_.FullName.Substring($site.Length + 1), $_.Length)
}
