<#
.SYNOPSIS
Uploads model/'s .onnx file as its own GitHub release, which CI fetches it from to build.

.DESCRIPTION
Run once per new graph, after its sha256 is written into
model/manifest.json, and before pushing that manifest (CI fetches the graph it names).

The release is tagged model-<first 12 hex digits of the sha256>, so a graph's tag never changes.
It is a pre-release, so GitHub's latest release is always an add-in release (v<VERSION>), which
is what installed copies update from.
Needs the GitHub CLI (gh), logged in with write access to the repo.

.EXAMPLE
powershell -File tools/dist/publish_model.ps1
#>
param(
    [string]$Repo = 'Pituchey-Hotam/Genizah'
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$manifest = Get-Content (Join-Path $root 'model\manifest.json') -Raw | ConvertFrom-Json
$file = $manifest.model_file
$sha = $manifest.model_sha256
$path = Join-Path $root "model\$file"

if (-not (Test-Path $path)) { throw "model/$file is missing: copy the graph in first" }
$got = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
if ($got -ne $sha) { throw "model/$file has sha256 $got, but model/manifest.json says $sha" }

$tag = 'model-' + $sha.Substring(0, 12)
# Windows PowerShell turns a native command's stderr into an error, which 'Stop' would throw.
$ErrorActionPreference = 'Continue'
gh release view $tag --repo $Repo *> $null
$exists = $LASTEXITCODE -eq 0
$ErrorActionPreference = 'Stop'
if ($exists) {
    Write-Host "Release $tag already exists"
    exit 0
}

$notes = @"
The classifier graph, not an add-in release. The add-in's builds name the graph they use in
model/manifest.json, and fetch it from here; each add-in release ships its own copy.

- file: $file
- sha256: $sha
- ONNX Runtime: $($manifest.onnxruntime)
"@
# The tag is created on the default branch; it only names the asset, the commit doesn't matter.
gh release create $tag $path --repo $Repo --title "Model $($sha.Substring(0, 12))" --notes $notes --prerelease
if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
