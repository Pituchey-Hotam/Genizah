# Worklog

## 2026-10-02 — Recall-hole patterns in `NameFinder`

The two pattern changes that waited for the add-in to use the labeller's finder are in:
`NameFinder` now takes Aramaic endings on אלהים (אלהא, אלהין, אלהך, אלהה, אלהכם, …) and a bare
relative ש before יהוה and אלהים, as `dataset/olah/extract/names.py` does (both datasets were
relabelled the same day). Vectors regenerated, with a hand-written case for each change; 122
tests pass on net48 and net7.0, including all 23,671 training windows and 9,327 texts.
→ `CLAUDE.md` (Layout: `NameFinder`)

## 2026-09-30 — Scan performance: position map, reused-row pane, benchmark

Word seemed to hang on large documents after scoring. The cause was indexing the whole
document's `Characters` once per name, at 0.66 s each on a 25,000-character document. A new
benchmark (`tools/benchmark`, a Debug-only COM hook plus per-phase `ScanTimings`) measured it
before anything changed. `PositionMap` maps offsets a paragraph at a time: the 25k scan went from
92 s to 14 s, with every range correct. The results pane now reuses a few rows instead of
building one control per result: 6.7 s down to 0.1–0.4 s. At 100k characters (861 names) a scan
takes 65–75 s, 85% of it scoring in the background. What's left freezes Word for about 8 s:
`new_result` and `censor_highlight`, about 9 ms per name. Progress updates cost another 4 s. The
400k size hasn't been run.
→ `CLAUDE.md` (Layout: `PositionMap`, `Results/`, `tools/benchmark`)

## 2026-09-24 — The classifier in the add-in

The add-in now scores every `Genizah.Core` candidate off the UI thread and censors, asks or
leaves it by two logit thresholds. The thresholds are empty application settings until the
owner sets them after classifier phase 4, and until then everything is Ask. The results pane
lists Ask first and lets each name be censored, left or restored. `NameInfo`'s regexes are gone.
`NameCensor` keeps a candidate's proclitics, suffixes and nikud when censoring, and a print
reuses the review if the text hasn't changed since. It builds with VS MSBuild, and the model
loads and scores in a .NET Framework x64 host. The first run in Word failed on a check that
refused to load when Office's own `onnxruntime.dll` (1.24) was already in the process. Tests
showed ORT binds our copy by full path anyway, from the folder `Microsoft.ML.OnnxRuntime.dll`
loads from. In Word, that folder is VSTO's shadow-copy cache, which has no native DLL, so the
loader now copies `onnxruntime.dll` there first.
→ `CLAUDE.md` (Layout)

## 2026-09-24 — ONNX Runtime inference in `Genizah.Core`

`NameClassifier` runs the shipping graph over a text's candidates, one window per run, since
batch-mates move a window's probability by up to 0.2 under the int8 layers' dynamic
quantization. Its logits match Python ORT to 1e-5 on net48 and net7.0, against logits
`make_vectors.py` now writes into `pipeline.json`. net48 needed a direct ORT reference and x64,
or it loads System32's older `onnxruntime.dll`. Not in the add-in yet; thresholds (classifier
phase 4) must be fitted on batch-1 scores.
→ `CLAUDE.md` (Layout, Rules)

## 2026-09-23 — The model's input pipeline, ported to C# (`Genizah.Core`)

New netstandard2.0 library: normalization with a raw-offset map, the labeller's candidate
finder, the BEREL WordPiece tokenizer and the 32-character window encoder. Its tests run on
net48 and net7.0 against vectors that `tools/make_vectors.py` generates from the Python sources
of truth. It matches all 23,604 training windows id for id, and every Olah/OSHB text's
candidates. Scanning normalized running text finds exactly OSHB's word-by-word candidates in all
929 chapters. Not wired into the add-in yet: ONNX Runtime inference is next.
→ `CLAUDE.md` (Layout), `tools/make_vectors.py`

## 2026-09-23 — Pending result-pane work committed

`SearchResult` now owns replacement, bookmark, highlight and undo. `NameInfo` patterns allow one
proclitic and nikud. The 12 touched files also went LF → CRLF in the editor. The
"range.Text workaround" stash is still unapplied.
→ commit `Move result logic into SearchResult; prefix- and nikud-aware name patterns`
