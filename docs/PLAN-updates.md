# Plan: offline installs and self-updates

Replaces the ClickOnce deployment described in `RELEASING.md`. When this ships, `RELEASING.md` is
rewritten to describe it and this file is deleted.

## Requirements

1. **Offline install.** Someone who has only the installer, and no internet, gets a working add-in,
   model included, the moment it finishes.
2. **Updates in the background.** Word never waits on a download. An update takes effect at the next
   Word start, code and model together, or not at all.
3. **Only download what changed.** A bugfix shouldn't re-download the ~100 MB model.
4. **Only run what we signed.** An update is accepted only if it was signed with our key.

## Why not ClickOnce

VSTO ties a ClickOnce install to the location it was installed from. An add-in installed from a
local folder (offline) can't be updated from a web URL: VSTOInstaller fails with "cannot be upgraded
from this location" (`AddInAlreadyInstalledException`). The usual ClickOnce workaround, a
`deploymentProvider` naming the web URL, is refused for Office add-ins ("You must not specify a
deployment provider for a ClickOnce application that specifies a custom host"). Microsoft documents
the rule and offers only uninstalling and reinstalling. For add-ins not installed through ClickOnce,
its docs say updates must be implemented in the add-in's own code. That's this plan.

## Design

### On disk, per user, without admin rights

```
%LOCALAPPDATA%\Genizah\
  versions\1.2.0\     a version's files, laid out as bin\Release: Genizah.vsto,
                      Genizah.dll.manifest, the DLLs, model\ (graph, vocabulary, thresholds)
  versions\1.3.0\     the next version, written by an update
  settings.json       the user's settings
```

Word loads the version that this registry value names:

- `HKCU\Software\Microsoft\Office\Word\Addins\Genizah`: `Manifest` =
  `file:///<versions>/1.2.0/Genizah.vsto|vstolocal`, `LoadBehavior` = 3, plus `FriendlyName` and
  `Description`. This is how a development build is registered too.
- `HKCU\Software\Microsoft\VSTO\Security\Inclusion\{guid}`: `Url` = that `.vsto` URL (without
  `|vstolocal`), `PublicKey` = our public key. One entry per version folder. Without it, VSTO
  refuses to load the add-in.

### What signs what

MSBuild signs the two manifests with our key:

- `Genizah.dll.manifest` lists every file of the version, with its size and sha256.
- `Genizah.vsto` names the version and pins `Genizah.dll.manifest` by its sha256.

The hashes are plain sha256 of the file bytes, base64-encoded.

What checks what, as tested:

- **VSTO, at every load:** refuses a manifest whose signature doesn't verify, or whose signer isn't
  the key in the version's inclusion entry. It does **not** check the files against the manifest:
  it loaded a version whose `Genizah.Core.dll` had been altered.
- **The updater, before switching versions:** checks everything else. It verifies both signatures
  against the public key built into the add-in, checks that the `.vsto` pins the manifest, and
  checks that every file has its manifest sha256.

Signature checks use `SignedXml` on each manifest's `StrongNameSignature`. ClickOnce writes
nonstandard algorithm URIs (`http://www.w3.org/2000/09/xmldsig#rsa-sha256` and `…#sha256`), which
must be registered with `CryptoConfig`. The check must also insist on a single reference with
`URI=""` and the enveloped-signature and exc-c14n transforms, so that a signature over only part of
the document doesn't pass.

### A release

GitHub release `v<VERSION>`, marked latest, holding:

- `Genizah.vsto` and `Genizah.dll.manifest`: the signed manifests. Together they also serve as the
  release's index.
- Every file the manifest lists, each as its own asset. Asset names can't contain folders, so a path
  is flattened (`model\vocab.txt` becomes `model.vocab.txt`).
- `Genizah-Setup-<VERSION>.exe`: the offline installer.

The model is one of the listed files. It goes back into the build, and the separate model store,
`model-*` releases-as-runtime-source and `release.json` go away. CI still fetches the graph from a
`model-<sha12>` release to build, since git can't hold it (`fetch_model.ps1`, `publish_model.ps1`).
Those releases are created with `--latest=false`, so `latest` is always an add-in release.

### From the build to the release, exactly

**The build.** A plain `msbuild -t:Build -p:Configuration=Release`, with no ClickOnce publish. That
build already writes signed manifests to `bin\Release`. Its `Genizah.vsto` has
`<deployment install="false"/>`, which is what a `vstolocal` registration loads. It points at
`Genizah.dll.manifest` in the same folder.

**What a version is.** The two manifests plus the files the `.dll.manifest` lists:
- its `dependentAssembly` elements with a `codebase` (the DLLs);
- its `file` elements (everything else).

Today that's 13 files:

```
Genizah.dll  Genizah.Core.dll  Microsoft.ML.OnnxRuntime.dll
Microsoft.Office.Tools.Common.v4.0.Utilities.dll  System.Buffers.dll  System.Memory.dll
System.Numerics.Vectors.dll  System.Runtime.CompilerServices.Unsafe.dll  onnxruntime.dll
model\manifest.json  model\model.int8-int4emb.onnx  model\thresholds.json  model\vocab.txt
```

Everything else in `bin\Release` is left out: the `.pdb`s, the `.lib`s and
`onnxruntime_providers_shared.dll`. That file is needed only for GPU execution providers. A folder
holding just the listed files and the two manifests was tested in Word: it loaded, and the CPU
runtime started.

**Flattening.** An asset's name is its manifest path with `\` replaced by `.`. The updater computes
the same name from the path, so there's no lookup table. The packaging step refuses a name that:
- has a character other than `A-Z a-z 0-9 . - _`, or starts or ends with `.` (GitHub renames such
  names);
- matches another name, ignoring case.

**`package.ps1`** reads `bin\Release` and writes `tools/dist/out/`:
- `assets/`: the two manifests plus the listed files under their flat names. This is exactly what the
  release holds, minus the installer.
- `version/`: the same files under their real paths. The installer packs this folder.

It checks that every listed file's size and sha256 match the manifest, and that the `.vsto` version
is `<VERSION>.0`.

**Uploading, atomically.** `release.yml` does this:
1. `gh release create v<VERSION> --draft`, then `gh release upload` for `assets/*` and the
   installer. A draft is invisible to `releases/latest`, so the updater can't see a half-uploaded
   release.
2. List the draft's assets through the API. Compare the list, names and sizes, with `assets/` plus
   the installer. Any difference fails the job, leaving the draft unpublished.
3. `gh release edit v<VERSION> --draft=false --latest`. This is the moment the release reaches users.

**Download URLs.** The updater uses:
- `https://github.com/Pituchey-Hotam/Genizah/releases/latest/download/Genizah.vsto`;
- `https://github.com/Pituchey-Hotam/Genizah/releases/download/v<version>/<flat name>`.

`<version>` is the `.vsto`'s version without its fourth part. Both URLs redirect to a signed CDN URL
that serves range requests and expires after about an hour, so a resumed download goes back to the
`github.com` URL.

**Limits.** GitHub allows up to 2 GiB per asset and 1000 assets per release.

### The update, once per Word session

It runs only in an installed copy (one running from `%LOCALAPPDATA%\Genizah\versions\`), and only
if the user hasn't turned it off. It starts a while after startup, off the UI thread, and never
bothers the user about a failure.

1. Download `releases/latest/download/Genizah.vsto`. Verify its signature. If its version isn't
   newer than the running one, clean up (below) and stop.
2. From that exact release (`releases/download/v<version>/`, not `latest`, which could move mid-update),
   download `Genizah.dll.manifest`. Check that its sha256 matches what the `.vsto` pins, and verify its
   signature.
3. Build `versions\<version>.partial\`. For each file the manifest lists, copy it from the running
   version if a file there has the same sha256, and otherwise download it (resumable, as the model
   download is today). Check each file's size and sha256. Refuse paths that are absolute or contain `..`.
4. Write the two manifests into the folder and rename it to `versions\<version>\`. A version folder
   only gets its final name once it's complete.
5. Add the inclusion entry for the new folder.
6. **Switch:** set `Addins\Genizah\Manifest` to the new folder. This one registry write is the update.
   Anything that fails before it leaves the installed version as it was.
7. Say so on Word's status bar and above the next scan's results, as now.

**Cleanup** runs with every check. It keeps three folders:
- the running version;
- the version the registry names;
- the latest release's `.partial` folder, so an interrupted download resumes in a later session.

It deletes every other folder, and removes the inclusion entries that point into deleted folders. A
lock file (`versions\update.lock`) keeps two Word processes from updating at once.

In `Genizah.Core`, `VersionStore` does steps 1–4, the cleanup and the lock; `Download` does the
resumable, verified downloads. The add-in does the registry, the timing and the notice.

**A broken update.** If a new version fails to load, Word disables the add-in, and the updater isn't
running to fix it. For now, the previous version's folder stays until the new version has started
once, so recovering is a single registry edit, or a reinstall. A "last version that loaded"
fallback can come later.

### The installer

Inno Setup, per user (`PrivilegesRequired=lowest`), fully offline. It holds one version's folder,
model included (~105 MB). It:

1. Writes the files to `versions\<VERSION>\`.
2. Writes the inclusion entry and the `Addins\Genizah` values, the same ones an update writes.
3. Registers its uninstaller in Add/Remove Programs.

Installing over an existing install adds the version folder and switches to it. Word doesn't need to
be closed, since the new folder is unused, but the new version only runs from the next Word start.

**Uninstall** removes `%LOCALAPPDATA%\Genizah`, the `Addins\Genizah` key, and every inclusion entry
whose URL points into that folder. That's done in Pascal script, matching on the URL.

The registry layout is written in two places, the installer script and the updater. Both point to
this section (and later `RELEASING.md`) as the one description of it.

### Settings

.NET's default user settings live in
`%LOCALAPPDATA%\Microsoft_Corporation\Genizah.vsto_<install location hash>\<Word build>\user.config`.
That means a new Word build starts from defaults today, and every version folder would too. They
move to `%LOCALAPPDATA%\Genizah\settings.json`: the per-name replacements and `check_for_updates`.
Nothing is imported from `user.config`, since there are no installed users yet.

## What goes away

- The ClickOnce publish, the Pages site and its deployment, the csproj's `InstallUrl` and update
  settings, and `IncludeModelGraph=false`.
- `release.json` and the `VSTOInstaller` call.
- The model store, `ReleaseInfo`, and `ModelLoader`'s download. The graph is beside the add-in again,
  in every build. `ModelStore`'s resumable, verified download is kept, generalised to any file.

## Steps

Each step is testable on its own. Nothing ships until step 7 passes.

1. **Settings.** Add `settings.json`. Switch `NameInfo` and the settings dialog to it, and drop
   .NET's user settings. Unit tests for reading and writing.
2. **Manifests (`Genizah.Core`).** Read the file list out of a `.dll.manifest`, and verify a signature
   against a pinned key, including the whole-document reference check. Tests use a committed signed
   pair from a real build, plus tampered and re-signed copies made at test time.
3. **Building a version (`Genizah.Core`).** Copy or download each file into `.partial`, check it, and
   rename the folder. Tests use the fake HTTP handler the store tests use, and cover copied files,
   downloaded files, bad hashes, bad paths and resuming.
4. **The updater (add-in).** The steps above plus the registry writes and cleanup, replacing the
   ClickOnce updater. `ModelLoader` goes back to loading the graph beside the add-in.
5. **Packaging and CI.** `package.ps1` and `release.yml`, as in "From the build to the release". The
   build includes the graph, and Pages is dropped.
6. **The installer.** `tools/dist/setup.iss`, built in CI with ISCC and attached to the release.
7. **End to end,** as with the ClickOnce tests: a scratch build pointed at a local server. Cover an
   offline install with no network, a code-only update (check that it doesn't download the graph), an
   update with a new graph, a tampered asset (refused, nothing switched), an impostor-signed release
   (refused), and uninstalling.
8. **Docs.** Rewrite `RELEASING.md` around this, update `CLAUDE.md` and `README.md`, delete this plan.

## Open questions

- **The signing key.** It will be replaced, at the owner's call. Its public half is built into the
  updater and the installer, so replacing it means shipping one update signed with the old key that
  trusts the new one.
- **Re-uploading the graph.** Every release uploads it again (~100 MB). That's simple. A release could
  instead point at an earlier release's asset for an unchanged file, at the cost of a lookup table.
- **The installer and SmartScreen.** An unsigned installer gets SmartScreen's "Windows protected your
  PC" warning when downloaded with a browser. Downloads made by the updater don't.
