# Genizah (the Word add-in)

See the workspace root's `../CLAUDE.md` for workflow conventions (scratch files, worklogs, docs
layout). This file covers only what's specific to the add-in. Start with `WORKLOG.md`.

**This repo is public** (`origin` is `Pituchey-Hotam/Genizah`). Never commit Olah text: not in
test data, fixtures, docs or commit messages. Committed test vectors come from OSHB (the Hebrew
Bible) or are written by hand. Full-data vectors live in the git-ignored
`Genizah.Core.Tests/testdata/local/`.

## Layout

- `Genizah/`: the VSTO Word add-in (.NET Framework 4.8, x64). It builds only with Visual
  Studio's MSBuild, not `dotnet build` (`MSBuild.exe Genizah/Genizah.csproj -restore`). The
  model files and `onnxruntime.dll` ship beside `Genizah.dll`, as ClickOnce content.
  - `Scanning/ModelLoader`: loads the classifier once per Word session, off the UI thread.
    Office ships its own ONNX Runtime (1.24, in `Office16\WinAppSDK` and the shared `AI\SDK`)
    and may load it into Word first. That's harmless: .NET Framework binds the
    `DllImport("onnxruntime")` by full path to the copy beside `Microsoft.ML.OnnxRuntime.dll`,
    so each copy stays separate. Checking for a loaded `onnxruntime.dll` by name finds
    Office's copy and gives a false alarm. VSTO shadow-copies the managed assemblies into
    `AppData\Local\assembly\dl3\…` without the native DLL, so the loader copies
    `onnxruntime.dll` there before the first ONNX Runtime call. The model is found through the
    add-in's `CodeBase`, not `Location`.
  - `Scanning/DocumentScanner`: the text the classifier sees (`Content.Text`, where offset i is
    `Characters[i + 1]`) and the Word range of each scored span. Scoring runs off the UI thread;
    everything else touches Word and runs on it.
  - `Scanning/PositionMap`: turns text offsets into Word ranges. It reads the document once as
    paragraphs, and converts offsets with arithmetic wherever a paragraph's text is as long as
    its positions. Only a paragraph where they differ (a field, say) indexes `Characters`, and
    only its own. Indexing the whole document's `Characters` costs more the further in the
    offset is: 0.66 s per name at 25,000 characters. If the paragraphs don't add up to the
    text, the scan falls back to that slow path.
  - `Scanning/ScanTimings`: where a scan's time went, phase by phase. It's written to the debug
    output after every scan. `Scanning/BenchmarkHook` (Debug builds only) lets
    `tools/benchmark` start a scan through `Application.COMAddIns("Genizah").Object`. It hands
    the scan to Word's UI thread, since calls from another process arrive on an RPC thread.
  - `Scanning/Verdict`: censor at or above `censor_logit_threshold`, leave below
    `leave_logit_threshold`, ask in between. Both are application settings in
    `Genizah.dll.config` (`app.config`), empty in the repo, and belong to the shipping fit. The
    owner sets them. Until both are set, every candidate is Ask.
  - `Results/`: one `SearchResult` per candidate (hidden bookmark, model verdict, and a state:
    pending, censored or left). The pane lists Ask first, then Censor, then Leave. It reuses
    just enough fixed-height `ResultControl` rows to fill the visible area, rebinding them as the
    list scrolls. One control per result cost about 50 ms and ten window handles each, and a
    process gets 10,000 handles.
  - `NameInfo.cs`: display names, base forms and the user's replacement per name. It no longer
    finds anything: candidates come from `Genizah.Core`.
- `Genizah.Core/`: netstandard2.0, with no Office dependency. It holds the model's input
  pipeline, ported from the Python that trained it:
  - `TextNormalizer`: `classifier/clf/normalize.py`, with an offset map back to the raw text.
    `NormalizedText.ToRaw` widens a span over trailing nikud, so replacing it leaves no
    orphaned mark.
  - `NameFinder`: the labeller's candidate finder (`dataset/olah/extract/names.py`
    `UNDASHED_PATTERNS`, scanned as `label/occurrences.py` `_undashed_pass`). It runs on
    normalized text, and name ids match `NameInfo.cs`.
  - `WordPieceTokenizer` and `WindowEncoder`: the fit's `tokenizer.json`, and `berel3.py`
    `encode_rows` (`normalize(left)[-32:] + target + normalize(right)[:32]`, `[CLS] … [SEP]`,
    128 ids, target mask by offset overlap). The tokenizer is exact only on normalized
    input, and it throws on anything else.
  - `NameClassifier`: runs the shipping ONNX graph (ONNX Runtime 1.30.0, the version the
    vectors are scored with) over every candidate in a raw text, returning each one's raw span
    and logit. It scores **one window per run**: the int8 layers quantize activations over the
    whole batch, so batching would make a name's score depend on the rest of the document (up
    to 0.2 in probability). Its logits match Python's to 1e-5 on net48 and net7.0. `Scan`
    scores lazily, for progress and early exit.
  - `NameCensor`: the censored text for a candidate. It finds the edit that turns the base
    form into the chosen replacement (אלהים → אלקים) and makes only that edit to the
    candidate's own letters, so proclitics, suffixes, extra matres and nikud stay
    (ואֱלֹהֵינוּ → ואֱלֹקינוּ). It isn't part of the model's pipeline contract.
- `Genizah.Core.Tests/`: xUnit, run on both net48 (the add-in's runtime and Regex engine) and
  net7.0: `dotnet test Genizah.Core.Tests`.
- `tools/make_vectors.py`: regenerates `model/vocab.txt`, `model/manifest.json` and every test
  vector, expected logits included, from the Python sources of truth
  (`py -3.13 tools/make_vectors.py`). It refuses a `tokenizer.json` whose configuration differs
  from what the C# port implements.
- `tools/benchmark/run_benchmark.ps1`: times a scan in Word, phase by phase, on OSHB documents
  of increasing size (`plain`, and `mixed`, which has a table and fields). It needs a Debug build
  loaded in Word, and it counts names mapped to the wrong range. Documents and timings go to the
  git-ignored `tools/benchmark/out/` (`results.jsonl`, one line per run, with a `-Label`). Pass
  several sizes with `powershell -Command`, not `-File`, which passes the list as one string.
- `model/`: `vocab.txt` and `manifest.json` (which fit, which graph, sha256s) are committed.
  The `.onnx` file is git-ignored; copy it in from the fit directory the manifest names.

## Rules that are easy to break

- **The pipeline is a cross-language contract.** A change to normalization, the patterns, the
  window or the tokenizer starts in the Python, and it changes what the model was trained on.
  Update the Python, rerun `tools/make_vectors.py`, then make the C# pass. Never edit a vector
  file by hand.
- **Run the local parity tests before committing a pipeline change.** They check every training
  window (23,604) and every Olah/OSHB text (9,327). They skip silently when `testdata/local/`
  is missing, so a green run on a fresh clone proves less.
- **No `\uXXXX` escapes for invisible characters in source.** Write code points as numbers
  (`(char)0x200F`) or, in Python, as `\N{NAME}`. Some editing tools turn escapes into the
  literal invisible character.
- **A .NET Framework project using `Genizah.Core` must reference `Microsoft.ML.OnnxRuntime`
  itself and build for x64.** The package copies its native DLL through `build/` props, which
  don't flow through a project reference and pick the DLL by `PlatformTarget`. Without them,
  Windows' own, older `onnxruntime.dll` in System32 is loaded and ONNX Runtime crashes on
  first use (`AccessViolationException` in `NativeMethods`).
- **JSONL and `vocab.txt` split on LF only.** The corpora and two vocabulary pieces contain
  characters that `str.splitlines` / `ReadAllLines`-style readers treat as line breaks.
