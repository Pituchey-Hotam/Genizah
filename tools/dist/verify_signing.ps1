# Check the actual manifest signatures against the key trusted by the updater and installer.
param([string]$BuildDir = 'Genizah/bin/Release')
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$source = Get-Content (Join-Path $root 'Genizah/Updater.cs') -Raw
$key = [regex]::Match($source, 'public const string PublicKey = "([^"]+)";').Groups[1].Value
$installer = Get-Content (Join-Path $root 'tools/dist/setup.iss') -Raw
if (!$key -or !$installer.Contains('#define PublicKey "' + $key + '"')) {
    throw 'Updater and installer must pin the same release public key'
}
# Use the updater's verifier, including its ClickOnce SHA256 algorithm registration.
Add-Type -Path (Join-Path $BuildDir 'Genizah.Core.dll')
$vsto = [IO.File]::ReadAllBytes((Join-Path $BuildDir 'Genizah.vsto'))
$deployment = [Genizah.Core.DeploymentManifest]::Read($vsto, $key)
$application = [IO.File]::ReadAllBytes((Join-Path $BuildDir 'Genizah.dll.manifest'))
[Genizah.Core.ApplicationManifest]::Read($application, $deployment, $key) | Out-Null
Write-Output 'Both manifests are signed by the pinned release key.'
