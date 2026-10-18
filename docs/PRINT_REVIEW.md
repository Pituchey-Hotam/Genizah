# Explicit review and quick print choices

The ribbon's **סקירת שמות** button starts a scan when the owning Word window has no completed
review. Subsequent clicks reopen existing results or ongoing progress, preserving decisions
and expansion state. A completed clean scan with zero results also counts as an existing
review. A failed scan can be retried by opening review. The compact **סרוק שוב** action sits
in a separate footer below the results and details and is hidden during scanning. Scanning
starts only through these user actions or the user's Check first choice from printing.

## Print path

Settings includes **בדוק את מצב הסקירה לפני הדפסה**, enabled by default, including for
existing settings files. Turning it off bypasses the print hook entirely: no text reads,
review checks or dialogs. Saving the setting applies immediately to every document in
the current Word session and persists for later sessions. Manual review remains available.

`GetPrintReviewStatus` examines existing in-memory review state. With no potentially trusted
review, it returns Unreviewed without even reading the document text. Otherwise it reads text
once and compares it with the reviewed snapshot. It never loads or runs a model, reconciles
individual Word ranges, starts background checking or silently changes document text.

| Status | Behavior |
|---|---|
| Current, no Pending names | Print directly |
| Current, Pending names | Print anyway / return to existing review / cancel |
| No current review | Print without checking / check first / cancel |
| Explicit scan running | Print without checking / return to progress / cancel |

`PrintReviewDialog` is owned by the document's current Word window and uses explicit action
labels. Closing it or pressing Escape cancels printing. A print choice does not grant a
persistent approval or establish a reviewed snapshot.

Check first cancels the current print, then queues the explicit full scan on Word's UI
context after the handler returns. It does not automatically restart printing; the results
show a reminder to print again after review. Returning to a pending review or running scan
opens that review's existing window, including when another view of the document initiated
printing. Neither action restarts a scan.

Existing reconciliation and print-review invalidation rules still apply. An edited document
can retain usable sidebar rows while requiring the unreviewed print choice. A completed clean
scan with zero results still supplies a current snapshot and avoids subsequent prompts for
the same text.

## Validation

`tools/benchmark/test_print_review.ps1` exercises the real handler without submitting print jobs.
Debug-only choice injection measures the hook independently of time spent answering a dialog;
UI Automation also clicks the real Print without checking button and closes the real dialog.
The script checks an unreviewed 132,001-character document, explicit empty scans, pending and
resolved decisions, edits before deferred reconciliation, pane reopening, Check first scans,
and reopening/printing while an explicit scan is already running.

The measured unreviewed decision took about 21 ms in the final run on Word 16.0 build
16.0.20430. Unlike the synchronous classification path, this case's work does not grow with
candidate count. A potentially current review still requires a text read and comparison;
normal Word/COM and document-size costs remain. Existing undo, reconciliation and multi-window
regressions cover the unchanged review lifecycle.
