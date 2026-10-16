<#
.SYNOPSIS
Downloads the shipping model into model/ from its GitHub release, and checks its sha256.

.DESCRIPTION
The .onnx file is too large for git, so it lives in its own GitHub release, tagged
model-<first 12 hex digits of its sha256> (see publish_model.ps1). model/manifest.json names
the file and its sha256, so this fetches exactly the graph the committed vectors and thresholds
were made for. Does nothing if model/ already holds that file. Used by CI; works the same on a
fresh clone, since the repo is public.

.EXAMPLE
powershell -File tools/dist/fetch_model.ps1
#>
param(
    [string]$Repo = $(if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'Pituchey-Hotam/Genizah' })
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$manifest = Get-Content (Join-Path $root 'model\manifest.json') -Raw | ConvertFrom-Json
$file = $manifest.model_file
$sha = $manifest.model_sha256
$path = Join-Path $root "model\$file"

function Sha256($p) { (Get-FileHash $p -Algorithm SHA256).Hash.ToLowerInvariant() }

if ((Test-Path $path) -and (Sha256 $path) -eq $sha) {
    Write-Host "model/$file is already the manifest's graph"
    exit 0
}

$tag = 'model-' + $sha.Substring(0, 12)
$url = "https://github.com/$Repo/releases/download/$tag/$file"
Write-Host "Downloading $url"
$tmp = "$path.part"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'  # Windows PowerShell's progress bar makes the download crawl
Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing

$got = Sha256 $tmp
if ($got -ne $sha) {
    Remove-Item $tmp
    throw "release $tag's $file has sha256 $got, but model/manifest.json says $sha"
}
Move-Item $tmp $path -Force
Write-Host "model/$file fetched and verified"
