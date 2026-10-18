# Sidebar reconciliation

`SearchResult` keeps its remembered uncensored decision (Pending or Left), current displayed
state and `IsStale` separately. Reconciliation reads only the existing range: censored text
means Censored; original text restores the remembered uncensored decision; anything else
makes the row stale. Equal original and censored spellings retain their explicit state,
because their text cannot distinguish a decision. No matching search or range repair occurs.

Word selection events schedule one deferred reconciliation per document. Window activation
also requests validation, while sidebar focus, selection and actions check current state
synchronously as needed. Document text is read once per pass; unchanged text skips the full
range walk unless forced by activation or scan completion. Scanning and the
add-in's own replacements suppress reentrant reconciliation. Selected rows are individually
validated before navigation and actions, including when the full text is unchanged.

Rows refresh in place through property notifications. Group expansion, list membership,
selection and scroll position are not rebuilt by reconciliation. Stale rows have a dimmed
status marker, an explanatory status/tooltip and no document navigation or action buttons;
they are excluded from normal counts and counted separately. Keyboard actions use the same
guards. A partial edit can become valid again after undo if Word retained the occurrence's
range. Batch undo and undo of a whole-name deletion can leave ranges permanently broken;
those rows remain unavailable until rescanned.

All existing panes for a document are checked when its text changes. Separate live ranges
for the very same occurrence can also be invalidated by a replacement in another window;
that other pane is disabled where necessary instead of retargeted. Different documents'
review state remains independent.

Print freshness is separate from row validity. An external text change invalidates the
session's reviewed-document snapshot, even if all tracked names are recognized. A later
sidebar action does not clear that invalidation; a fresh successful scan does. Printing only
compares potentially current review snapshots with current text, without reconciling Word ranges
or classifying names. Without a matching review, it offers printing unchecked, checking first,
or cancelling. Only explicitly choosing Check first starts a scan, after cancelling that print.

## Validation

`tools/benchmark/test_reconciliation.ps1` exercises real scan results and the production
sidebar action method through opt-in Debug hooks in disposable Word documents. It verifies
individual undo/redo, remembered Leave decisions, restoration undo/redo, partial-edit
disable/recovery, blocked stale actions, unchanged undo history, conservative print freshness,
rapid undo/redo, and separate panes for one document. It also checks batch undo/redo leaves
broken ranges disabled and that rescanning replaces them.

Synthetic seeded review rows are Debug-only fixtures for establishing two independent
panes over the same original text; they do not change model thresholds or persisted settings.
The existing undo and window regressions remain relevant. Programmatic `Range.Text` assignments
do not themselves guarantee a selection event, so simulated manual edits in the test explicitly
move the selection as a user edit would; sidebar interaction provides another validation path.
