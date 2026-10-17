# Genizah

A Word add-in that finds divine names in Hebrew documents and censors them. Once written, such a
name can't be casually erased or discarded, so a document meant to be thrown away should carry a
modified spelling instead (אלהים → אלקים).

Not every occurrence is a divine name: אל is also "to", and אלהים can mean judges or foreign gods.
So a classifier scores each candidate in context, and the add-in censors it, leaves it, or asks
you.

## Names in scope

| id | name |
|---|---|
| `havaya` | יהוה |
| `elohim` | אלהים |
| `eloha` | אלוה |
| `el` | אל |
| `adnut` | אדני |
| `tsvaot` | צבאות |
| `ehye` | אהיה |
| `shaday` | שדי |

`havaya` is always censored. The others are censored or left when the model is confident, and
asked about otherwise.

## Using it

**צנזר שמות** on the גניזה ribbon group scans the document. The results pane groups the names into
Ask, Censor and Leave, with each one's context and the model's probability that it's divine.
Selecting a row selects the name in the document.

Each Word window has its own results pane and review state. Scanning another document preserves
the first window's results and pane visibility; scans requested in different windows run in order.

Keyboard: the arrows move, Enter/Space opens or closes a group, צ (or C) censors and ה (or L)
leaves or undoes a censor, then moves to the next row.

The group's dialog launcher opens **הגדרות גניזה**, where you choose each name's replacement and
whether the add-in updates itself.

## Building

Requires Visual Studio with the Office/SharePoint development workload.

```
powershell -File tools/dist/fetch_model.ps1      # the model graph (~100 MB, not in git)
MSBuild.exe Genizah/Genizah.csproj -restore      # not `dotnet build`: VSTO needs MSBuild
dotnet test Genizah.Core.Tests                   # net48 and net7.0
```

To install a release, run `Genizah-Setup-<version>.exe` from the
[latest release](https://github.com/Pituchey-Hotam/Genizah/releases/latest). It works offline,
model included, and the add-in then updates itself in the background. See
[`docs/RELEASING.md`](docs/RELEASING.md).

## Layout

- `Genizah/`: the VSTO add-in (.NET Framework 4.8, x64): ribbon, scanning, results pane.
- `Genizah.Core/`: the classifier's input pipeline and ONNX scoring, with no Office dependency
  (netstandard2.0 and net48).
- `Genizah.Core.Tests/`: xUnit, against test vectors generated from the training code.
- `model/`: vocabulary, manifest and per-name thresholds. The `.onnx` graph is fetched.
- `tools/`: release scripts (`dist/`) and a scan benchmark that drives Word (`benchmark/`).

Releases: [`docs/RELEASING.md`](docs/RELEASING.md).
