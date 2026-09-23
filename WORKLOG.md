# Worklog

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
