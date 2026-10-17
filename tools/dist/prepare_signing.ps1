# Prepares a manifest signer for the reusable build workflow. Never writes a private key
# inside the checkout or passes a password on a command line.
param([bool]$ReleaseSigning = $false)
$ErrorActionPreference = 'Stop'
if ($ReleaseSigning) {
    if (!$env:GENIZAH_SIGNING_PFX -or !$env:GENIZAH_SIGNING_PASSWORD) {
        throw 'Release signing requires GENIZAH_SIGNING_PFX and GENIZAH_SIGNING_PASSWORD'
    }
    $pfxPath = Join-Path $env:RUNNER_TEMP ('signing-' + [guid]::NewGuid() + '.pfx')
    try {
        [IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($env:GENIZAH_SIGNING_PFX))
        $password = ConvertTo-SecureString $env:GENIZAH_SIGNING_PASSWORD -AsPlainText -Force
        $cert = Import-PfxCertificate -FilePath $pfxPath -Password $password -CertStoreLocation Cert:\CurrentUser\My
    }
    finally {
        if (Test-Path -LiteralPath $pfxPath) { Remove-Item -LiteralPath $pfxPath }
    }
}
else {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Genizah CI only' `
        -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeySpec Signature `
        -Provider 'Microsoft Enhanced RSA and AES Cryptographic Provider' `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddDays(7)
}
"thumbprint=$($cert.Thumbprint)" >> $env:GITHUB_OUTPUT
if (!$cert.HasPrivateKey -or $cert.NotAfter -le (Get-Date)) { throw 'Signing certificate is expired or lacks its private key' }
if ($ReleaseSigning) {
    $root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
    $source = Get-Content (Join-Path $root 'Genizah/Updater.cs') -Raw
    $expected = [regex]::Match($source, 'public const string PublicKey = "([^"]+)";').Groups[1].Value
    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($cert)
    try {
        if (!$expected -or $rsa.ToXmlString($false) -cne $expected) {
            throw 'Release certificate does not match the public key pinned in Updater.cs'
        }
    }
    finally { $rsa.Dispose() }
}
