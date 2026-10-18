# Live Word ranges through undo and redo

Measured 2026-10-18 on Word 16.0, build 16.0.20430, using disposable unsaved documents.

## Result

Live ranges reliably retained their exact text and boundaries through individual replacements,
including length changes and single replacements inside a custom undo record. They did not
survive undoing a custom record containing multiple replacements. Real Genizah scan results
showed the same batch failure. Redo did not repair those broken ranges.

| Scenario | Exact range observations | Total observations |
|---|---:|---:|
| Individual equal-length, shorter and longer replacements | 108 | 108 |
| Same replacements inside single-decision custom undo records | 108 | 108 |
| Restoring a censored name, then undoing/redoing that restoration | 36 | 36 |
| Insertions outside names: prefix, gap and suffix | 108 | 108 |
| Partial edit within a name | 36 | 36 |
| Deleting a whole name | 32 | 36 |
| Multiple replacements in one custom undo record | 12 | 36 |
| Real Genizah scan with three names | 6 | 30 |

There were 498 observations in total: 446 exact and 52 mismatches. The mismatches are
behavioral measurements, not harness failures. Every measured document's complete text was
independently checked against its expected state before checking its tracked ranges.

## What batch undo does

The synthetic batch replaced `ABCD`, `EFGH`, `ABCD` with shorter, longer and equal-length
tokens in a single custom undo record. All ranges were correct immediately after replacement.
After undo, the first two ranges collapsed at the first name's start; the third range grew
to cover all three names and the intervening text. Redo retained that broken topology.

The real scan used three occurrences of the Hebrew Havaya name, censored to the installed
replacement `ה'`. Its actual `SearchResult.Range` objects behaved identically. For example,
the first range became `[4,4)` instead of `[4,8)`, and the third became `[4,26)` instead of
`[22,26)` after undo. These are broken positions, not just stale cached states.

Deleting a whole name is another boundary: its range collapses, and undo does not expand it
back over the restored name. Other names' ranges remain correct. A collapsed range's null
`Text` is normalized to an empty string in the measurements; deletion itself is therefore
counted as a correctly collapsed range, not as an exact match to an actionable name.

## Method and reproduction

The synthetic probe creates all three duplicate ranges before editing, then uses the exact
`SearchResult.Replace` sequence: read start, assign `Range.Text`, and call `SetRange` with the
new length. It measures the initial state, edited state and two undo/redo cycles through both
COM `Undo`/`Redo` and native ribbon commands via `ExecuteMso`.

The document contains repeated spellings at distinct positions. Each observation compares
both range boundaries and text with independently known positions; no searching, relocation
or repair is performed. The real-scan checks use a read-only Debug hook to inspect the actual
results retained by the add-in.

```powershell
powershell -NoProfile -File tools/benchmark/probe_live_ranges.ps1
# Just single-decision custom records and restoration:
powershell -NoProfile -File tools/benchmark/probe_live_ranges.ps1 -SidebarOnly
```

The archived matrix has 354 observations; the focused sidebar follow-up has 144. Raw scan
snapshots and CSVs are retained locally under `scratch/2026-10-18/live-ranges/` (git-ignored).
The probe above regenerates the measurements.

## Reconciliation implications

For individual review decisions, the simple rule is supported: recognize the original or
censored spelling at the existing range, update row state, and never search for a similar
occurrence. Partial manual edits can preserve range geometry while making the row's text
unrecognized, so geometry alone is insufficient. A row whose text cannot be trusted should
be disabled rather than silently retargeted.

Undoing the initial scan can reasonably end its review session and clear the sidebar instead
of trying to preserve the batch's broken ranges. This is a product policy, not evidence that
the original classifier predictions are wrong: undo restores the text originally scored.
Reliable recognition of that session boundary still needs implementation and testing.

This probe does not implement reconciliation. Coverage is limited to one Word build, plain
main-story paragraphs, COM and ribbon-command undo/redo; keyboard event delivery is measured
separately in [UNDO_EVENTS.md](UNDO_EVENTS.md). Tables, tracked changes, multi-window actions
and arbitrary manual edits were not covered by the range probe.
