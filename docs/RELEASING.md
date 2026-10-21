# Releasing

## How installs and updates work

- **Install:** `Genizah-Setup-<VERSION>.exe` from the latest GitHub release. It installs per user,
  needs no admin rights or network, and includes the model.
- **Update:** once per Word session, the add-in reads the latest release. If it's newer, the add-in
  builds that version's folder beside the running one:
  - unchanged files are copied;
  - changed ones are downloaded;
  - every file is checked against the release's signed manifests.

  It then points Word at the new folder for the next start. A failure changes nothing and isn't
  shown; `update.log` records it. The settings dialog can turn updates off, and shows the running
  version, a version installed for the next start, and the last check's time and result.

Not ClickOnce: VSTO can't update a ClickOnce install from anywhere but where it was installed from,
so an offline install could never update from the web.

### On disk

```
%LOCALAPPDATA%\Genizah\
  versions\<version>\   Genizah.vsto, Genizah.dll.manifest, the DLLs, model\
  versions\<version>.partial\   being built; renamed when complete
  versions\update.lock  one updater at a time
  settings.json
  update.log            each check's outcome, the only trace of a failed update
```

Registry (HKCU), written by both `tools/dist/setup.iss` and `Genizah/Registration.cs`:

- `Software\Microsoft\Office\Word\Addins\Genizah`: `Manifest` =
  `file:///<versions>/<version>/Genizah.vsto|vstolocal`, which is the version Word loads,
  `LoadBehavior` = 3.
- `Software\Microsoft\VSTO\Security\Inclusion\{guid}`: `Url` (the `.vsto` URL) and `PublicKey`. Each
  version folder needs one entry; without it, VSTO won't load the add-in.

Each check deletes the folders other than three:
- the running version;
- the version the registry names;
- the latest release's `.partial` folder.

It also deletes their inclusion entries. So the previous version stays until the new one has
started once.

### Signing

MSBuild signs `Genizah.vsto` and `Genizah.dll.manifest` with a private RSA-3072 release key.
Its self-signed code-signing certificate expires on 2036-10-17; it authenticates updates through
the pinned public key, and does not provide a publicly trusted Windows publisher identity.
The private key is never committed. GitHub repository Actions secrets hold:

- `GENIZAH_SIGNING_PFX`: base64 of the password-protected PFX.
- `GENIZAH_SIGNING_PASSWORD`: the PFX password.

Only `release.yml` passes these secrets to the reusable build with `release-signing: true`.
`prepare_signing.ps1` imports the certificate into the runner's user store, deletes the temporary
PFX, and checks its public key against the updater's pin. MSBuild receives the thumbprint;
`verify_signing.ps1` then verifies both manifest signatures and the installer's matching pin.
The workflow removes the imported private key even if building or verification fails.
Missing secrets, an expired certificate, or a wrong key fail the release build.

CI uses a disposable certificate and receives no release secrets, including for fork PRs.
Its artifacts are for testing; the release workflow rebuilds and signs its own artifacts.

For a local release build, import the release PFX into `Cert:\CurrentUser\My`; the project
defaults to thumbprint `35DA11DF051232402B8603F8274B3B36837F62AB`.
Contributors can create a disposable certificate and override the thumbprint instead:

```powershell
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Genizah development' `
    -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeySpec Signature `
    -Provider 'Microsoft Enhanced RSA and AES Cryptographic Provider' -CertStoreLocation Cert:\CurrentUser\My
msbuild Genizah/Genizah.csproj -restore -p:Configuration=Release "-p:ManifestCertificateThumbprint=$($cert.Thumbprint)"
```

- `Genizah.vsto` pins the `.dll.manifest` by sha256.
- The `.dll.manifest` lists every file's size and sha256.

VSTO checks the signatures but not the files, so the updater checks both before switching
(`Genizah.Core/Manifests.cs`). The public key is built into `Updater.cs` and `setup.iss`. A new key
must first ship in an update signed with the old one.

## Cutting a release

A release is a PR into `master`:

1. Bump `VERSION`, the only place the version lives.
2. If the model graph changed, publish it first (below).
3. Open the PR. CI builds, tests, packages, builds the installer, and checks that `VERSION` is
   new.
4. Merge. CI uploads `v<VERSION>` as a draft, checks the uploaded assets against the build, then
   publishes it as latest. If the check fails, the release stays a draft.

## What a release holds

- `Genizah.vsto` and `Genizah.dll.manifest`.
- Every file the manifest lists, named by its path with `\` replaced by `.`
  (`model\vocab.txt` becomes `model.vocab.txt`).
- `Genizah-Setup-<VERSION>.exe`.

`tools/dist/package.ps1` makes these from a plain Release build. It writes `out/assets/` for the
release, and `out/version/` with the real layout for the installer. It refuses a file that doesn't
match the manifest, and a name that GitHub would change.

## Publishing a new model graph

Git can't hold the graph, so CI fetches it from its own release, `model-<first 12 hex of its
sha256>`. With the new `.onnx` in `model/` and `model/manifest.json` updated:

    powershell -File tools/dist/publish_model.ps1

It's a pre-release, so it never becomes the latest release. `tools/dist/fetch_model.ps1` fetches
the graph, for CI or a fresh clone.
