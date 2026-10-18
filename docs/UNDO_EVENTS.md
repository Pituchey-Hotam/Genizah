# Word undo/redo event coverage

Measured 2026-10-18 using Word 16.0, build 16.0.20430 and the workspace Debug add-in.

## Result

All 60 verified undo/redo operations emitted exactly one `WindowSelectionChange` event.
In 36 operations, selection start and end stayed unchanged despite the event firing.
None of the 30 idle controls emitted an event. No operation emitted `DocumentChange`,
`WindowActivate`, `WindowDeactivate` or `WindowSize` during its observation window.

| Route | Verified undo/redo operations | With selection event | With unchanged selection coordinates |
|---|---:|---:|---:|
| COM `Document.Undo` / `Redo` | 20 | 20 | 12 |
| Ribbon commands via `ExecuteMso` | 20 | 20 | 12 |
| Keyboard Ctrl+Z / Ctrl+Y | 20 | 20 | 12 |

This supports using `WindowSelectionChange` to schedule sidebar reconciliation on this
Word build, even when the caret appears not to move. The event handler must not discard
an event merely because its selection coordinates equal the preceding event's coordinates.

## Method

Each route tested equal-length, shorter and longer range replacements, bold formatting,
and a Genizah scan that groups two Hebrew-name replacements into one undo record.
Each scenario ran with the caret away from the edit and with a selection at the edit.
Each combination measured idle, undo and redo separately in disposable unsaved documents.

The Debug-only probe subscribes on explicit request, records event names and timestamps
without querying Word inside the callbacks, and unsubscribes when stopped. The script lets
preparation notifications drain, clears the log, records the starting selection, performs
the operation, waits 700 ms, and captures the events and ending selection. It independently
checks document text and formatting against the expected state before counting an observation.

The initial matrix produced 82 successful observations before a keyboard formatting undo
failed the expected-document-state check. Isolated keyboard formatting and grouped-scan
runs each completed all six observations; a final six-observation formatting run also passed
after hardening the probe's foreground-input guard. Ten observations duplicate earlier cases,
so the combined data contains 100 successful observations covering 90 unique cases.
The cause of the failed keyboard attempt was not established, and that attempt is excluded
from coverage rather than treated as a missing event.

Raw logs and the combined CSV are archived locally under
`scratch/2026-10-18/undo-events/` (git-ignored). The probe below regenerates the measurements.

## Reproduction

Build the Debug add-in, then run:

```powershell
powershell -NoProfile -File tools/benchmark/probe_undo_events.ps1
```

The script creates and closes its own visible Word instance. Keyboard cases require that
instance to have foreground focus; the script refuses to send keys to another window.
`-Routes Keyboard -Scenarios formatting` isolates the keyboard formatting cases.
Outputs go to a fresh temporary directory unless `-OutputDirectory` is supplied.

## Implication and limits

Use a deferred, coalesced `WindowSelectionChange` handler to compare current document text
with the last reconciled snapshot. Reconcile result states, row controls and counts only
when text differs, and refresh other panes for the same document. The handler should only
read the document and update sidebar state; it must not write document formatting or modify
Word's undo history.

Validate a result again before applying a sidebar action, and reconcile when its pane or
window becomes active. If live ranges no longer identify the expected text, report stale
results and require a rescan rather than guess which occurrence was edited.

These measurements cover one installed Word build. The ribbon tests invoke its native
commands rather than physically clicking buttons; the undo dropdown, other Office builds,
protected documents, and arbitrary manual-edit notification coverage were not tested.
This experiment does not implement the synchronization fix. Actual live-range reliability
is measured separately in [LIVE_RANGE_UNDO.md](LIVE_RANGE_UNDO.md): individual decisions retain
their ranges, while whole-scan undo can collapse or expand them.
