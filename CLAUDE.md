# Genizah

What the add-in does, how to build and test it, and the top-level layout are in `README.md`.
Releases, installs, updates and CI: `docs/RELEASING.md`.

## Code map

- `Genizah/` (VSTO, net48, x64). The version is the root `VERSION`, stamped in at build time.
  We ship our own `onnxruntime.dll` beside `Genizah.dll`; `Scanning/ModelLoader` copies it into
  VSTO's shadow-copy folder and finds the model files, graph included, through `CodeBase`, not
  `Location`.
  - `Updater`: once per session, builds the latest release's version folder beside the running
    one and switches Word's registration to it (`Registration`); see `docs/RELEASING.md`. Each
    check's outcome goes to `%LOCALAPPDATA%\Genizah\update.log`, whose last real check the settings
    dialog shows beside the running version. It forces TLS 1.2: an add-in's
    AppDomain otherwise offers only SSL 3/TLS 1.0, which GitHub refuses.
  - `Scanning/DocumentScanner`: the classifier sees the text of every story Word prints
    (`PrintedStories`: the body, footnotes and endnotes, text boxes and shapes, grouped ones
    included, then each section's unlinked headers and footers; not review comments), joined by
    paragraph marks. Headers and footers are read around their fields, whose results Word works
    out per page and reads differently from one moment to the next. Text boxes come before
    headers because censoring follows this order, and with a page number in the footer, one undo
    record that edits a header and then a text box in it can't undo the text box. Within a
    story, offset i is `Characters[i + 1]`, except that a content control's marks are characters
    with no text. Scoring runs off the UI thread; anything touching Word runs on it.
    Every range the scan reads text or characters from goes through `Plain` (field results,
    never codes, whatever the view), and its text through `WordText.ToUnicode`: Word reads its
    optional and non-breaking hyphens, note marks and column breaks as control characters,
    which are mapped one for one to Unicode before the pipeline. Censoring writes hyphens as
    Word's own non-breaking hyphen (`WordText.ForWord`); U+2011 falls back to another font in
    most Hebrew fonts.
  - `Scanning/PositionMap`: offsets → Word ranges, per story, by paragraph arithmetic. A range
    in another story is made from that story's range (`DocumentScanner.Span`), never
    `doc.Range`, which is always the body. Avoid indexing a whole story's `Characters`; it's slow
    far into a document. Where a paragraph has to, it steps past content control marks. Beside a
    field, `Characters` widens a character over the field's hidden marks and code, and over a
    floating shape's anchor before a paragraph's first letter; and a paragraph after a field's
    end starts with the previous paragraph's mark. `DocumentScanner.Bounds` and `Chunk.Skip`
    undo these, so censoring a link's text or a TOC entry edits only the letters, not the field,
    and censoring a name that starts a paragraph doesn't delete the shape anchored there.
  - `Scanning/Verdict`: censor at or above `censor_at`, leave below `leave_below`, ask between
    (from `model/thresholds.json`). `censor_always` names are always censored; a name without
    thresholds, or a missing/invalid file, means ask.
  - `UiSynchronizationContext`: the UI thread's context, which every await and post in the add-in
    resumes through; each callback runs under it, since the thread's own context doesn't last
    between calls into the add-in. It drops callbacks once the add-in shuts down: when Word quits
    mid-scan, a continuation posted from the thread pool through WinForms' own context would throw
    there and crash Word, which can then disable the add-in (`tools/benchmark/test_quit_during_scan.ps1`).
  - `Scanning/InputLock`: disables Word's window while the Word-side stages run, so the text
    can't change under the offsets.
  - `Results/`: the WPF results pane (`ResultsView.xaml`) inside a WinForms task pane. The list
    is flat and virtualized; shortcuts match the typed character, so they follow the layout.
    Results track live Word ranges, without document bookmarks. Review status stays in the sidebar;
    selecting a result selects its range in Word. Never use document formatting for review UI.
    Selection events defer and coalesce read-only reconciliation: original/censored spellings update
    status, unrecognized ranges disable only that row. Explicit Leave decisions persist separately;
    external text changes invalidate print-review freshness until a fresh scan.
    The ribbon starts a scan on first opening, then reopens this window's existing review
    or progress. A compact footer action explicitly rescans, including after a clean scan. Printing
    reads the printed text, compares it with cached review snapshots and, with no current review,
    asks only if `NameFinder.HasCandidate` finds a name (the scan scores only what the finder
    finds); it offers a choice through `PrintReviewDialog`, and never runs inference or walks Word
    result ranges. Check first cancels printing and schedules a scan. `ReadText` restores
    `Saved`: reading an existing empty header's `Range` marks the document unsaved.
  - `CensorSettingsDialog.xaml`, `Theme.xaml`, `NameInfo.cs` (per-name display names, base forms
    and replacements). `AboutDialog.xaml`, opened from the settings dialog and by itself on the
    first ribbon click (`about_shown` in the user's settings), holds the halachic disclaimer and
    the credits; keep its credits in step with `NOTICE`.
  - `AddInSettings`: the user's settings, in `%LOCALAPPDATA%\Genizah\settings.json`
    (`Genizah.Core/UserSettings`), shared by every version and Word build.
  - `Contributing`, `ContributeDialog.xaml`: whenever there are eligible unsent decisions, a link in the
    pane's footer offers to share the user's decisions with the
    collection server (README, "Sharing decisions"); it sits beside rescan on the same row.
    The action reads "דווח טעות אחת" or "דווח N טעויות". After the first report, a small "הצג"
    link opens the record-by-record dialog. The first visible offer before reporting gets a
    gentle blue highlight that fades out (`contribution_hint_shown`, once per installation).
    Closing the pane never opens an explanation. Until the user first
    shares (`Contributing.Explained`: an install id exists), the link opens a popup explaining
    sharing, with the record-by-record `ContributeDialog` and a "share automatically" box
    (`share_automatically`, off by default); after that it sends directly. A record is sent only for a decision made in the pane (`SearchResult.Revisions`)
    that answers an ask or reverses the model, never one reversing a `censor_always` name; its
    text is only the model's window (`ModelWindow`, from `WindowEncoder.Window`, the same slicing
    the scan encodes). A failed send keeps its batch and ids for the retry, which the server
    deduplicates. Sent records' hashes go to `%LOCALAPPDATA%\Genizah\contributed.json` so a rescan
    doesn't offer them again. The server is `Contributing.BuiltInEndpoint`, or the settings'
    `contribution_endpoint` for testing (https, or http on loopback); with neither, the pane and
    the settings show nothing about contributions.
- `Genizah.Core/` (netstandard2.0, and net48 so the add-in gets `SignedXml` from the framework):
  a port of the Python input pipeline the model was trained with, which isn't in this repo; the
  test vectors stand in for it. `TextNormalizer` → `NameFinder` → `WordPieceTokenizer`/
  `WindowEncoder` → `NameClassifier`. Outside the pipeline:
  `NameCensor` (makes only the base→replacement edit, keeping affixes and nikud), `WordText`, `Quote`,
  `NameThresholds`, `ModelManifest`, `Contributions` (which decisions become records, the wire
  format the collection server validates, its client, and `SentLog`). For updates: `Manifests` (reads a version's signed `.vsto`
  and `.dll.manifest` after checking the signer's key; ClickOnce's algorithm names need
  registering with `CryptoConfig`), `VersionStore` (builds version folders), `Download`, `UpdateLog` (one tab-separated line per check).
- `Genizah.Core.Tests/`: `testdata/` vectors are from the Hebrew Bible or hand-written.
  `LocalParityTests` need undistributed data and skip.
- `model/`: `vocab.txt`, `manifest.json`, `thresholds.json` and the fetched `.onnx` belong
  together; a new graph comes with its own vocabulary, thresholds and vectors.
- `tools/benchmark/`: times a scan in a running Word (Debug build). Pass several sizes with
  `powershell -Command`, not `-File`. The `test_*.ps1` scripts are Word regression tests through
  the Debug COM hook. `make_example.ps1` builds the example document: prose on page one; on page
  two the name in every printed place outside the body text (tables, text boxes, shapes,
  header, footer, notes) plus a review comment, which isn't printed and must be left; on page
  three the name beside fields and shape anchors (links, a table of contents, a page number, a
  text box). `test_example.ps1` scans it with field codes hidden and shown, and checks each
  place, that every field, link and shape survives, selecting each result from the pane, leaving
  and censoring again from it, and undoing and redoing the scan in one step.

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
