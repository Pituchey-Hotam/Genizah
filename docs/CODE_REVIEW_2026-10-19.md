# Add in code review

Review date: 2026-10-19. This report records the add-in on `develop` at commit `7a41876` before the resulting fixes; it is a dated review, not a living description of current behavior.

The overall architecture is worth retaining. The main risks lie at the boundary between a scored text snapshot and the live Word document. The classifier pipeline is comparatively straightforward; scan lifecycle and review state are harder to reason about.

## Findings

### 1 Censoring can modify the prefix and leave the name intact

**High severity; reproduced.** `NameCensor.NameStart` finds the name's first letter after allowable prefix letters. For שדי, that first letter can itself be a prefix. The candidate finder recognizes `שבשדי`, but the default replacement produces `ש-בשדי` instead of `שבש-די`. The result is then marked Censored.

Derive the name's start from a complete match of the prefix and name structure instead of guessing from the first letter. Add regression coverage for prefixed forms.

Source: [NameCensor.cs](../Genizah.Core/NameCensor.cs), `NameStart` and `Apply`.

### 2 A mapping mismatch still permits editing the wrong text

**High severity; source finding.** When a mapped Word range differs from the scored substring, `ApplyResults` downgrades automatic censorship to Ask. `SearchResult` then adopts the mismatched range's text as its `OriginalText`. Its subsequent safety checks accept that text, and clicking Censor can replace it using the original candidate's name.

For example, if the classifier scores a divine name but position mapping returns unrelated text, the scan notices the mismatch. However, the result stores the unrelated text as its baseline. Clicking Censor checks whether the range still equals that baseline, so the check succeeds even though the range never matched the scored occurrence.

A mapping mismatch should make the row uneditable or fail the scan. Pass the scored substring into the result as an independent expected value; asking the user does not repair the mapping.

Disposition after discussion: deferred as low-priority defensive hardening. No normal Word interaction reproducing an initial mapping mismatch was established; the existing gray-row behavior correctly handles text changed after result creation. Revisit if real scans report mapping mismatches or the position mapper changes.

Sources: [ThisAddIn.cs](../Genizah/ThisAddIn.cs), `ApplyResults`; [SearchResult.cs](../Genizah/Results/SearchResult.cs), constructor and `Replace`.

### 3 Printable stories outside the main body are not reviewed

**High severity if print review is intended to cover the whole printable document; scope decision required.** Scanning and freshness checks read `doc.Content.Text`, covering the main story. Headers, footers, footnotes and text boxes can contain untouched names while the print hook considers the review current. Edits in those areas also leave the compared snapshot unchanged.

Either scan the supported printable stories and include them in freshness checks, or make the limited review scope explicit in the product.

Sources: [DocumentScanner.cs](../Genizah/Scanning/DocumentScanner.cs), `ReadText`; [ThisAddIn.cs](../Genizah/ThisAddIn.cs), `GetPrintReviewStatus`.

### 4 The input lock protects only one document window

**Medium severity; source finding.** `InputLock` disables only `doc.ActiveWindow`. Another window showing the same document remains editable while the application stage yields to repaint. The initial text comparison occurs before later yields and cannot protect against subsequent edits during mapping and replacement.

Lock every window displaying the document during application and prevent sidebar edits for that document. The scan semaphore only serializes scans.

Sources: [InputLock.cs](../Genizah/Scanning/InputLock.cs), constructor; [ThisAddIn.cs](../Genizah/ThisAddIn.cs), `ApplyResults`.

### 5 Incomplete threshold entries silently become automatic decisions

**Medium severity; reproduced.** Loading `{"names":[{"name":"el"}]}` succeeds with both thresholds equal to zero. Missing numeric fields receive serializer defaults, changing the decision policy instead of triggering the documented Ask fallback.

Require both numeric fields on each entry. Also validate the thresholds' model identity at runtime: `ModelSha256` is loaded but never checked by the add-in. Release verification reduces the risk, but runtime loading accepts mismatched files.

Sources: [NameThresholds.cs](../Genizah.Core/NameThresholds.cs), `Load` and `Entry`; [Verdict.cs](../Genizah/Scanning/Verdict.cs), `Thresholds.Load`.

### 6 Updater cleanup relies on file locks to protect running versions

**Medium severity; source finding.** Cleanup preserves this process's running version and the registered version, then recursively deletes the others. An older Word process can still be running another version. VSTO shadow-copies assemblies, so source-folder locks are not a reliable lifetime marker. Recursive deletion can also remove some files before encountering a locked file.

The older process loads thresholds from its installation directory on subsequent scans. Use a per-version lifetime lease held by each Word process and skip cleanup while that lease is held.

Sources: [Updater.cs](../Genizah/Updater.cs), `Clean`; [VersionStore.cs](../Genizah.Core/VersionStore.cs), `Clean`.

### 7 Routine navigation repeatedly reads the entire document

**Medium severity; source finding without a new performance measurement.** Selection events schedule reconciliation, which reads all document text before deciding whether anything changed. Posting coalesces pending callbacks but does not throttle sustained selection events. Sidebar focus, selection and actions also request reconciliation, sometimes repeatedly for one operation.

Debounce document-wide reconciliation, retain immediate validation of the selected range, and keep the synchronous freshness check before printing.

Sources: [ThisAddIn.cs](../Genizah/ThisAddIn.cs), `QueueReconciliation` and `ReconcileDocument`; [ResultsView.xaml.cs](../Genizah/Results/ResultsView.xaml.cs), selection and action handlers.

## Architecture recommendations

- **Separate document review state from window presentation state.** Each window owns results and freshness snapshots even when several windows show the same document. Keep pane visibility, selection and expanded groups per window; keep the review session and scan lifecycle per document.
- **Return `Task` from the scan operation and add lifetime cancellation.** Keep `async void` at the ribbon event boundary. Closing a document currently leaves scoring running until it eventually fails against the closed document. Cancellation should also cover queued scans and shutdown.
- **Move edit orchestration out of the WPF view.** `ResultsView` starts Word undo records, calls global add-in state, edits ranges and triggers freshness updates. A small review controller would make operation ordering explicit and testable.
- **Keep results provisional until application succeeds.** The previous results are cleared before mapping and replacement finish. A later failure can leave partial edits and partial internal results behind an error message. Define and expose partial failure deliberately.

Sources: [ThisAddIn.cs](../Genizah/ThisAddIn.cs), `WindowResults`, `CensorNames` and `ApplyResults`; [ResultsView.xaml.cs](../Genizah/Results/ResultsView.xaml.cs), `Act`.

## Smaller fixes

- Candidate overlap detection checks every accepted candidate for every hit. Since hits are sorted by start and accepted spans never overlap, checking the last accepted span is sufficient. See [NameFinder.cs](../Genizah.Core/NameFinder.cs), `Find`.
- A settings file containing valid JSON `null` returns a null settings object; this was reproduced. Treat a null deserialization result as defaults. See [UserSettings.cs](../Genizah.Core/UserSettings.cs), `Load`.

## Design choices to retain

Retain the Core/Office separation, pipeline parity vectors, conservative handling of unrecognized live ranges, and flat virtualized results list. Each has a clear purpose. Prioritize the replacement bug and mapping guard, then settle story coverage and consolidate review ownership.

## Validation

`dotnet test Genizah.Core.Tests --no-restore --verbosity minimal` passed 206 tests on .NET Framework 4.8 and 206 on .NET 7, with no skips. The prefix, missing-threshold and null-settings cases were checked separately against the compiled .NET Framework core assembly. No source changes were made during the review, and live Word integration probes were not run for this review.
