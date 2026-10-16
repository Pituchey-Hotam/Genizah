# Releasing

## How installs and updates work

Each release is a GitHub release, `v<VERSION>`, that holds the whole version. There's no server
of our own and no ClickOnce install.

- **Installing.** `Genizah-Setup-<VERSION>.exe` from the latest release installs for the current
  user, without admin rights and without a network. The add-in works, model included, from the
  next Word start.
- **Updating.** Once per Word session, about 30 s after startup, the add-in reads the latest
  release. If it's newer, the add-in builds that version's folder beside the running one:
  - unchanged files are copied from the running version;
  - only changed files are downloaded, so a code fix doesn't re-download the ~100 MB model;
  - every file is checked against the release's signed manifests.

  Then it switches Word to the new folder. The new code and model run together from the next Word
  start. A failure at any point leaves the installed version as it is, and the next session tries
  again. The settings dialog can turn updates off.
- **Only signed releases install.** The updater accepts a release only if its manifests are signed
  with our key, whose public half is built into the add-in. Every file must also have the size and
  sha256 that the manifests list.

### On disk, per user

```
%LOCALAPPDATA%\Genizah\
  versions\1.2.0\       a version: Genizah.vsto, Genizah.dll.manifest, the DLLs, model\
  versions\1.3.0\       the next version, written by an update
  versions\1.3.0.partial\   a version being built; renamed when complete
  versions\update.lock  held while an update runs, so two Word processes don't both update
  settings.json         the user's settings, shared by every version
```

Word loads the version this registry value names, the same way a development build is registered:

- `HKCU\Software\Microsoft\Office\Word\Addins\Genizah`: `Manifest` =
  `file:///<versions>/1.2.0/Genizah.vsto|vstolocal`, `LoadBehavior` = 3, plus `FriendlyName` and
  `Description`. Switching versions means changing `Manifest`.
- `HKCU\Software\Microsoft\VSTO\Security\Inclusion\{guid}`: `Url` is that `.vsto` URL, without
  `|vstolocal`, and `PublicKey` is our public key. There's one entry per version folder. Without
  it, VSTO refuses to load the add-in.

The URL is percent-encoded, as .NET's `Uri.AbsoluteUri` writes it, so a profile path with spaces or
Hebrew letters works. The installer (`tools/dist/setup.iss`) and the updater (`Registration.cs`)
both write these values.

Each check also cleans up. It keeps the running version's folder, the folder the registry names,
and the latest release's `.partial` folder, so an interrupted download resumes later. It deletes
the rest, along with their inclusion entries. This means the previous version stays until the new
one has started once. If a new version fails to load, Word disables the add-in, and recovering
takes pointing `Manifest` back at the previous folder, or reinstalling.

### What signs what

MSBuild signs both manifests in `bin\Release` with `Genizah/Genizah_TemporaryKey.pfx`:

- `Genizah.dll.manifest` lists every file of the version, with its size and sha256.
- `Genizah.vsto` names the version and pins `Genizah.dll.manifest` by its sha256.

VSTO checks a manifest's signature and signer every time it loads. It does **not** check the files
against the manifest, so the updater checks them before switching. That check is in
`Genizah.Core/Manifests.cs`. It verifies each manifest's `StrongNameSignature` with `SignedXml`
against the pinned key, and requires that the signature covers the whole document.

### Why not ClickOnce

VSTO ties a ClickOnce install to the location it was installed from. An offline install, from a
local folder, can't be updated from the web ("cannot be upgraded from this location"). The
workaround, a `deploymentProvider`, is refused for Office add-ins. Microsoft says add-ins installed
some other way must update themselves in code, which is what this design does. It still uses
ClickOnce's manifests: MSBuild writes and signs them, and VSTO checks them.

## Cutting a release

A release is a PR into `master`.

1. Bump `VERSION` (MAJOR.MINOR.PATCH). It's the only place the version lives. The build stamps it
   into `Genizah.dll` and both manifests.
2. If the model graph changed, publish it first (below), or CI can't fetch it.
3. Open the PR. CI builds, runs the tests, packages, builds the installer, and checks that
   `VERSION` is greater than every release.
4. Merge. CI creates `v<VERSION>` with notes from the merged PRs:
   - it uploads to a **draft** first, which `releases/latest` never shows;
   - it checks the draft's asset names and sizes against the build;
   - only then does it publish the release as latest, which is when it reaches installed copies.

   If the check fails, the release stays a draft. Rerunning the workflow replaces the draft.

Pushing to `master` without bumping `VERSION` fails instead of overwriting a release.

## What a release holds

- `Genizah.vsto` and `Genizah.dll.manifest`: the signed manifests, which are also the index.
- Every file the `.dll.manifest` lists, each as its own asset. Asset names can't contain folders,
  so `\` becomes `.`: `model\vocab.txt` is uploaded as `model.vocab.txt`. The updater derives the
  same name from the path.
- `Genizah-Setup-<VERSION>.exe`: the offline installer.

The updater reads `https://github.com/Pituchey-Hotam/Genizah/releases/latest/download/Genizah.vsto`.
It then fetches everything else from that exact release, `releases/download/v<version>/<asset>`,
so a newer release published mid-update can't mix in.

**The build.** It's a plain Release build, not a ClickOnce publish. `tools/dist/package.ps1` reads
`bin\Release` and writes two folders:
- `out/assets/`: the release's assets;
- `out/version/`: the same files under their real paths, which the installer packs.

The script refuses:
- a file whose size or sha256 doesn't match the manifest;
- a build whose version isn't `VERSION`;
- a build without the graph;
- a name GitHub would rename (anything but `A-Z a-z 0-9 . - _`, or a leading or trailing `.`);
- two names that differ only in case.

Files outside the manifest, such as `.pdb`s, `.lib`s and `onnxruntime_providers_shared.dll`
(needed only for GPU providers), aren't shipped.

## Publishing a new model graph

Git can't hold the graph, so CI fetches it from its own GitHub release,
`model-<first 12 hex of its sha256>`, before building. Once `model/manifest.json` and the test
vectors are regenerated for the new graph, with its `.onnx` in `model/`:

    powershell -File tools/dist/publish_model.ps1

This checks the file against the manifest and creates the `model-<sha12>` release, which is never
marked "latest", so `latest` is always an add-in release. It needs `gh`, logged in with write
access. `tools/dist/fetch_model.ps1` downloads and verifies the manifest's graph; CI uses it, and
so can a fresh clone.

## Signing

The manifests are signed with `Genizah/Genizah_TemporaryKey.pfx`, a self-signed key. Its public
half is built into `Genizah/Updater.cs` and `tools/dist/setup.iss`. Replacing the key means first
shipping an update, signed with the old key, that trusts the new one.

The installer itself isn't signed. A browser download of it gets SmartScreen's "Windows protected
your PC" warning. Downloads made by the updater don't.

## Where it lives in the code

- `Genizah/Updater.cs`: the once-per-session check. It only runs in an installed copy, one running
  from `%LOCALAPPDATA%\Genizah\versions\`, never in a development build.
- `Genizah/Registration.cs`: the registry values above.
- `Genizah.Core/VersionStore.cs`: builds version folders, cleans up, and holds the lock.
- `Genizah.Core/Download.cs`: resumable, redirect-following, verified downloads. GitHub redirects
  each asset to a signed CDN URL that expires after about an hour, so a resumed download starts
  again from the `github.com` URL.
- `Genizah.Core/Manifests.cs`: reads and verifies the signed manifests.
- `tools/dist/package.ps1`, `tools/dist/setup.iss`, `.github/workflows/`: packaging, the installer
  and CI.
