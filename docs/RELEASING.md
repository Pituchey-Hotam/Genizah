# Releasing

A release is a PR into `master`. When it merges, `.github/workflows/release.yml` builds and tests
the add-in and publishes the GitHub release `v<VERSION>`, marked latest.

## Cutting a release

1. On `develop` (or a branch off it), bump `VERSION` (MAJOR.MINOR.PATCH). It is the only place
   the version lives: `Genizah.csproj` stamps it into `Genizah.dll` (`<VERSION>.0`) and into the
   `.vsto` and `.dll.manifest` identities.
2. If the model graph changed, publish it first (below), or CI can't fetch it.
3. Open a PR from `develop` into `master`. `ci.yml` builds, runs the tests, and checks that
   `VERSION` is new and greater than every `v*` tag.
4. Merge. The release gets notes generated from the PRs since the previous `v*` tag.

A push to `master` whose `VERSION` is already released fails the release job instead of
overwriting anything.

## What a release carries

- `Genizah-<VERSION>.zip`: `bin\Release` without symbols, import libraries or the model graph.
  Its `model/` has `vocab.txt`, `thresholds.json` and `manifest.json`.
- `model-manifest.json`: a copy of `model/manifest.json`, so an update check can see which graph
  a release needs without downloading the zip.

The graph is not in the release. It's the GitHub release `model-<first 12 hex of its sha256>`,
which `manifest.json`'s `model_sha256` names. Add-in releases that share a graph share that one
download. Release builds pass `-p:IncludeModelGraph=false`. The signed `.dll.manifest` lists
every file VSTO must find, so the graph has to be left out of the build, not just the zip.
`tools/dist/package.ps1` refuses a build that lists it.

## A new model graph

Once `model/manifest.json` and the test vectors are regenerated for the new graph, with its `.onnx`
in `model/`:

```
powershell -File tools/dist/publish_model.ps1
```

It checks the local `.onnx` against the manifest, then creates the `model-<sha12>` release with
`--latest=false`, so GitHub's "latest release" stays an add-in release. It needs `gh`, logged in
with write access. Without `gh`, create the release by hand on GitHub with the same tag. Attach
the `.onnx` under its manifest file name and untick "Set as the latest release".

`tools/dist/fetch_model.ps1` downloads the manifest's graph into `model/` and verifies its
sha256. CI uses it, and it works on a fresh clone too.

## Signing

The manifests are signed with `Genizah/Genizah_TemporaryKey.pfx`, a self-signed Visual Studio key
that expired in June 2024. MSBuild still signs with it, but Office trusts the add-in only after
a prompt. Changing the certificate later changes the publisher identity that installed copies
trust.

## Not built yet

- Installing from a release. The zip has no graph, so double-clicking its `.vsto` installs an
  add-in that can't find its model.
- An update check: read the latest release, compare `VERSION`, and compare `model-manifest.json`'s
  `model_sha256` with the installed graph's.
