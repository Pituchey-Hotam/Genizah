# Releasing

## How installs and updates work

The add-in is a ClickOnce deployment on GitHub Pages:

    https://pituchey-hotam.github.io/Genizah/Genizah.vsto

The model graph (~100 MB) is not part of it. Each graph is its own GitHub release, tagged
`model-<first 12 hex of its sha256>`, and `model/manifest.json` names the graph a given version
of the add-in needs.

- **Installing.** Open the `.vsto` URL and accept the trust prompt. Accepting it adds the URL to
  VSTO's inclusion list, which is what lets later updates install silently.
- **Updating.** VSTO's own update check is off, so Word never waits on a download at startup.
  Instead the add-in checks Pages itself. When a newer version is out, it first downloads that
  version's model in the background and checks its sha256, and only then runs
  `VSTOInstaller.exe /i <url> /s`. The new code and its model take effect together at the next
  Word start. If anything fails halfway, the installed version and its model stay as they are.
- **Code and model always match.** The add-in only ever uses the graph its own manifest names.
  If it starts without that graph (a plain install, or Pages moved on to a newer version during an
  update), it downloads the graph and can only scan once it's there.

The `.vsto` URL is permanent: every install trusts and updates from exactly that address. Renaming
the repo, moving it to another owner, or changing the publishing certificate cuts existing installs
off from updates.

## Cutting a release

A release is a PR into `master`.

1. Bump `VERSION` (MAJOR.MINOR.PATCH). It is the only place the version lives; the build stamps it
   into `Genizah.dll` and the ClickOnce manifests.
2. If the model graph changed, publish it first (below), or CI can't fetch it.
3. Open the PR. CI builds, runs the tests, and checks that `VERSION` is greater than every release.
4. Merge. CI publishes the ClickOnce deployment to Pages and creates the GitHub release
   `v<VERSION>` with notes from the merged PRs.

Pushing to `master` without bumping `VERSION` fails instead of overwriting a release.

## What gets published

- **Pages:** `Genizah.vsto`, `Application Files/Genizah_<version>/`, and `release.json`
  (the version and the model it needs). Only the latest version is kept there.
- **GitHub release `v<VERSION>`:** the notes, plus `release.json`.
- **GitHub release `model-<sha12>`:** the graph, published once per graph and shared by every
  add-in version that uses it.

## Publishing a new model graph

Once `model/manifest.json` and the test vectors are regenerated for the new graph, with its
`.onnx` in `model/`:

    powershell -File tools/dist/publish_model.ps1

This checks the file against the manifest and creates the `model-<sha12>` release (never marked
"latest"). It needs `gh`, logged in with write access. `tools/dist/fetch_model.ps1` downloads and
verifies the manifest's graph; CI uses it, and so can a fresh clone.

## One-time setup

In the repo's settings, under Pages, set the source to **GitHub Actions**.

## Signing

The manifests are signed with `Genizah/Genizah_TemporaryKey.pfx`, a self-signed key. Installs show
an "unknown publisher" prompt once. Since installs trust this key, replacing it later means
existing installs stop updating unless they first add the new key to their inclusion list.

## Not built yet

- The add-in's update check, model download and silent install.
- A setup `.exe` that includes the model, so a fresh install works without a download.
