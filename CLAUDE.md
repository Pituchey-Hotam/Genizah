# Genizah

What the add-in does, how to build and test it, and the top-level layout are in `README.md`.
Releases, installs, updates and CI: `docs/RELEASING.md`.

## Code map

- `Genizah/` (VSTO, net48, x64). The version is the root `VERSION`, stamped in at build time.
  We ship our own `onnxruntime.dll` beside `Genizah.dll`; `Scanning/ModelLoader` copies it into
  VSTO's shadow-copy folder and finds the model files, graph included, through `CodeBase`, not
  `Location`.
  - `Updater`: once per session, builds the latest release's version folder beside the running
    one and switches Word's registration to it (`Registration`); see `docs/RELEASING.md`.
  - `Scanning/DocumentScanner`: the classifier sees `Content.Text`, where offset i is
    `Characters[i + 1]`. Scoring runs off the UI thread; anything touching Word runs on it.
  - `Scanning/PositionMap`: offsets → Word ranges, by paragraph arithmetic. Avoid indexing the
    whole document's `Characters`; it's slow far into a document.
  - `Scanning/Verdict`: censor at or above `censor_at`, leave below `leave_below`, ask between
    (from `model/thresholds.json`). `censor_always` names are always censored; a name without
    thresholds, or a missing/invalid file, means ask.
  - `Scanning/InputLock`: disables Word's window while the Word-side stages run, so the text
    can't change under the offsets.
  - `Results/`: the WPF results pane (`ResultsView.xaml`) inside a WinForms task pane. The list
    is flat and virtualized; shortcuts match the typed character, so they follow the layout.
    Results track live Word ranges, without document bookmarks. Review status stays in the sidebar;
    selecting a result selects its range in Word. Never use document formatting for review UI.
    Selection events defer and coalesce read-only reconciliation: original/censored spellings update
    status, unrecognized ranges disable only that row. Explicit Leave decisions persist separately;
    external text changes invalidate print-review freshness until a fresh scan.
  - `CensorSettingsDialog.xaml`, `Theme.xaml`, `NameInfo.cs` (per-name display names, base forms
    and replacements).
  - `AddInSettings`: the user's settings, in `%LOCALAPPDATA%\Genizah\settings.json`
    (`Genizah.Core/UserSettings`), shared by every version and Word build.
- `Genizah.Core/` (netstandard2.0, and net48 so the add-in gets `SignedXml` from the framework):
  a port of the Python input pipeline the model was trained with, which isn't in this repo; the
  test vectors stand in for it. `TextNormalizer` → `NameFinder` → `WordPieceTokenizer`/
  `WindowEncoder` → `NameClassifier`. Outside the pipeline:
  `NameCensor` (makes only the base→replacement edit, keeping affixes and nikud), `Quote`,
  `NameThresholds`, `ModelManifest`. For updates: `Manifests` (reads a version's signed `.vsto`
  and `.dll.manifest` after checking the signer's key; ClickOnce's algorithm names need
  registering with `CryptoConfig`), `VersionStore` (builds version folders), `Download`.
- `Genizah.Core.Tests/`: `testdata/` vectors are from the Hebrew Bible or hand-written.
  `LocalParityTests` need undistributed data and skip.
- `model/`: `vocab.txt`, `manifest.json`, `thresholds.json` and the fetched `.onnx` belong
  together; a new graph comes with its own vocabulary, thresholds and vectors.
- `tools/benchmark/`: times a scan in a running Word (Debug build). Pass several sizes with
  `powershell -Command`, not `-File`.
  `probe_undo_events.ps1` measures opt-in Debug undo/redo event coverage; results and limits
  are in `docs/UNDO_EVENTS.md`.
  `probe_live_ranges.ps1` checks live range boundaries through individual and grouped undo/redo;
  see `docs/LIVE_RANGE_UNDO.md` before relying on ranges after whole-scan undo.

## Rules

- **The pipeline must match training exactly.** A change to normalization, candidate finding,
  the window or the tokenizer changes every score. Vectors in `testdata/` and `model/` are
  generated: never edit them by hand, and don't bend the pipeline to make a test pass.
- **Score one window per `Run`.** The int8 layers quantize over the batch, so batching makes a
  name's score depend on the rest of the document.
- **Compare thresholds as doubles.** They sit between float logits; a float could round onto one.
- **No `\uXXXX` escapes for invisible characters.** Write `(char)0x200F`; some tools turn
  escapes into the literal invisible character.
- **A .NET Framework project using `Genizah.Core` references `Microsoft.ML.OnnxRuntime` itself
  and builds x64.** Otherwise its native DLL isn't copied and Windows' older one gets loaded.
- **Split JSONL and `vocab.txt` on LF only.** Some vocabulary pieces contain other line breaks.
