# Genizah (the Word add-in)

See the workspace root's `../CLAUDE.md` for workflow conventions (scratch files, worklogs, docs
layout). This file covers only what's specific to the add-in. Start with `WORKLOG.md`.

**This repo is public** (`origin` is `Pituchey-Hotam/Genizah`). Never commit Olah text: not in
test data, fixtures, docs or commit messages. Committed test vectors come from OSHB (the Hebrew
Bible) or are written by hand. Full-data vectors live in the git-ignored
`Genizah.Core.Tests/testdata/local/`.

## Layout

- `Genizah/`: the VSTO Word add-in (.NET Framework 4.8). It builds only with Visual Studio's
  MSBuild, not `dotnet build`. Today it finds names with `NameInfo.cs`'s regexes and replaces
  every match; the classifier isn't wired in yet.
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
    to 0.2 in probability). Its logits match Python's to 1e-5 on net48 and net7.0.
- `Genizah.Core.Tests/`: xUnit, run on both net48 (the add-in's runtime and Regex engine) and
  net7.0: `dotnet test Genizah.Core.Tests`.
- `tools/make_vectors.py`: regenerates `model/vocab.txt`, `model/manifest.json` and every test
  vector, expected logits included, from the Python sources of truth
  (`py -3.13 tools/make_vectors.py`). It refuses a `tokenizer.json` whose configuration differs
  from what the C# port implements.
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
