using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;
using Genizah.Core;
using Genizah.Results;
using Genizah.Scanning;
using Microsoft.Office.Tools;
using Word = Microsoft.Office.Interop.Word;

namespace Genizah
{
    public partial class ThisAddIn
    {
        private sealed class WindowResults
        {
            public Word.Document Document;
            public readonly List<SearchResult> ResultsList = new List<SearchResult>();
            public readonly ResultsControl Control = new ResultsControl();
            public CustomTaskPane Pane;
            public string ReviewedText;
            public string ObservedText;
            public bool ReviewInvalidated;
            public bool Scanning;
            public bool HasCompletedReview;
        }

        private readonly Dictionary<int, WindowResults> windows = new Dictionary<int, WindowResults>();
        private readonly Dictionary<Word.Document, bool> pendingReconciliations = new Dictionary<Word.Document, bool>();
        private bool reconciling;
        private bool editingResult;
        private bool shuttingDown;
        private const string Title = "צנזור גניזה";
        // Word's undo record and screen updating are application-wide: serialize scans.
        private readonly SemaphoreSlim scanGate = new SemaphoreSlim(1, 1);
        internal UiSynchronizationContext UiContext { get; private set; }

        /// <summary>Set once an update is installed for the next start: shown above the session's scan results.</summary>
        private string updateNotice;

        private void ThisAddIn_Startup(object sender, EventArgs e)
        {
            UiContext = new UiSynchronizationContext();
            Application.WindowActivate += Application_WindowActivate;
            Application.WindowSelectionChange += Application_WindowSelectionChange;
            Application.DocumentChange += RemoveClosedWindows;
            Application.DocumentBeforePrint += new Word.ApplicationEvents4_DocumentBeforePrintEventHandler(Application_DocumentBeforePrint);
            // Off the UI thread, so the first scan doesn't wait for it.
            ModelLoader.StartLoading();
            _ = Updater.CheckAsync(version => UiContext.Post(_ => AnnounceUpdate(version), null));
        }

        /// <summary>Says so quietly: on Word's status bar now, and above the results of the next scan.</summary>
        private void AnnounceUpdate(Version version)
        {
            updateNotice = $"גרסה {version} של גניזה הותקנה, ותפעל מההפעלה הבאה של Word.";
            try { Application.StatusBar = updateNotice; }
            catch (Exception) { }
        }

        private void ThisAddIn_Shutdown(object sender, EventArgs e)
        {
            shuttingDown = true;
            // A scan still loading the model or scoring stops where it is, as does anything else posted.
            UiContext.Close();
            Application.WindowSelectionChange -= Application_WindowSelectionChange;
            foreach (var state in windows.Values.ToList()) RemoveWindow(state);
        }

        private WindowResults GetResults(Word.Window window)
        {
            RemoveClosedWindows();
            if (windows.TryGetValue(window.Hwnd, out var state)) return state;
            state = new WindowResults { Document = window.Document };
            state.Pane = CustomTaskPanes.Add(state.Control, "תוצאות", window);
            state.Control.ResultChanged += (sender, args) => RecordReview(state);
            state.Control.ReviewRequested += (sender, args) => ReconcileDocument(state.Document);
            state.Control.ScanRequested += (sender, args) => CensorNames(state.Document, window: window);
            windows.Add(window.Hwnd, state);
            return state;
        }

        /// <summary>Opens this window's review, starting its first scan when needed.</summary>
        public void ShowReview(Word.Window window)
        {
            var state = GetResults(window);
            if (!state.Scanning && !state.HasCompletedReview)
            {
                CensorNames(state.Document, window: window);
                return;
            }
            state.Pane.Visible = true;
            QueueReconciliation(state.Document);
        }

        private void Application_WindowActivate(Word.Document doc, Word.Window window)
        {
            RemoveClosedWindows();
            QueueReconciliation(doc, true);
        }

        private void Application_WindowSelectionChange(Word.Selection selection)
        {
            try { QueueReconciliation(selection.Document); }
            catch (System.Runtime.InteropServices.COMException) { }
        }

        private void QueueReconciliation(Word.Document doc, bool force = false)
        {
            if (shuttingDown || !windows.Values.Any(state => state.Document == doc && !state.Scanning && state.ResultsList.Count > 0)) return;
            if (pendingReconciliations.TryGetValue(doc, out bool queuedForce))
            {
                pendingReconciliations[doc] = force || queuedForce;
                return;
            }
            pendingReconciliations.Add(doc, force);
            UiContext.Post(_ =>
            {
                bool requestedForce = pendingReconciliations[doc];
                pendingReconciliations.Remove(doc);
                if (!shuttingDown) ReconcileDocument(doc, requestedForce);
            }, null);
        }

        private void ReconcileDocument(Word.Document doc, bool force = false)
        {
            if (reconciling || editingResult || shuttingDown) return;
            reconciling = true;
            try
            {
                string text = DocumentScanner.ReadText(doc);
                foreach (var state in windows.Values.ToList())
                {
                    if (state.Document != doc || state.Scanning || state.ResultsList.Count == 0) continue;
                    if (!force && state.ObservedText == text) continue;
                    if (state.ObservedText != text)
                    {
                        // Matching tracked names does not establish that all document text was reviewed.
                        state.ReviewInvalidated = true;
                        state.ReviewedText = null;
                    }
                    state.ObservedText = text;
                    bool changed = false;
                    foreach (var result in state.ResultsList) changed |= result.Reconcile();
                    if (changed) state.Control.RefreshResults();
                }
            }
            catch (System.Runtime.InteropServices.COMException) { } // Document closed or Word is temporarily busy.
            finally { reconciling = false; }
        }

        internal void BeginResultEdit() => editingResult = true;

        internal void EndResultEdit(Word.Document doc)
        {
            editingResult = false;
            QueueReconciliation(doc);
        }

        private void RemoveClosedWindows()
        {
            // Keep a closing window's control alive until the scan awaiting scoring finishes.
            var open = new HashSet<int>();
            foreach (Word.Window window in Application.Windows) open.Add(window.Hwnd);
            foreach (var pair in windows.ToList())
                if (!pair.Value.Scanning && !open.Contains(pair.Key))
                {
                    windows.Remove(pair.Key);
                    RemoveWindow(pair.Value);
                }
        }

        private void RemoveWindow(WindowResults state)
        {
            ClearResults(state);
            CustomTaskPanes.Remove(state.Pane);
            state.Control.Dispose();
        }

        private PrintReviewStatus GetPrintReviewStatus(Word.Document doc, out WindowResults review, out int pending)
        {
            pending = 0;
            review = windows.Values.FirstOrDefault(state => state.Document == doc && state.Scanning);
            if (review != null) return PrintReviewStatus.Scanning;
            var possible = windows.Values.Where(state => state.Document == doc
                && !state.ReviewInvalidated && state.ReviewedText != null
                && !state.ResultsList.Any(result => result.IsStale)).ToList();
            if (possible.Count == 0) return PrintReviewStatus.Unreviewed;
            // Read text only when there is a potentially current review to verify.
            // No model work or Word range walk is allowed in this hook.
            string text = DocumentScanner.ReadText(doc);
            review = possible.FirstOrDefault(state => state.ReviewedText == text);
            if (review == null) return PrintReviewStatus.Unreviewed;
            pending = review.ResultsList.Count(result => result.State == ResultState.Pending);
            return pending == 0 ? PrintReviewStatus.Current : PrintReviewStatus.Pending;
        }

        private void Application_DocumentBeforePrint(Word.Document doc, ref bool cancel)
        {
            if (!AddInSettings.Current.CheckBeforePrinting) return;
            try
            {
                var status = GetPrintReviewStatus(doc, out var review, out int pending);
#if DEBUG
                lastPrintStatus = status;
#endif
                if (status == PrintReviewStatus.Current) return;
                var window = doc.ActiveWindow;
                var reviewWindow = review == null ? window : (Word.Window)review.Pane.Window;
#if DEBUG
                var choice = printChoiceOverride != null ? printChoiceOverride(status, pending) : PrintReviewDialog.ShowOver(window, status, pending);
#else
                var choice = PrintReviewDialog.ShowOver(window, status, pending);
#endif
                if (choice == PrintReviewChoice.Print) return;
                cancel = true;
                if (choice != PrintReviewChoice.Review) return;
                // Finish cancelling this print before opening review or starting an explicit scan.
                int hwnd = reviewWindow.Hwnd;
                UiContext.Post(_ =>
                {
                    foreach (Word.Window open in Application.Windows)
                        if (open.Hwnd == hwnd)
                        {
                            open.Activate();
                            if (status == PrintReviewStatus.Unreviewed)
                                CensorNames(doc, "לאחר הסקירה יש להדפיס שוב.", window: open);
                            else
                                ShowReview(open);
                            return;
                        }
                }, null);
            }
            catch (Exception ex)
            {
#if DEBUG
                if (probingPrint) throw;
#endif
                if (MessageBox.Show("לא ניתן היה לוודא שהסקירה עדכנית: " + ex.Message + "\nלהדפיס ללא בדיקה?",
                                    Title, MessageBoxButtons.YesNo) == DialogResult.No)
                    cancel = true;
            }
        }

        /// <summary>
        /// Scores every candidate name in the document (off the UI thread), censors those the
        /// model is sure of, and lists them all in the results pane.
        /// </summary>
        /// <param name="notice">Shown above the results, if given.</param>
        /// <param name="finished">Called with where the scan's time went, however it ended.</param>
        public async void CensorNames(Word.Document doc, string notice = null, Action<ScanTimings> finished = null, Word.Window window = null)
        {
            var state = GetResults(window ?? doc.ActiveWindow);
            if (state.Scanning) return;
            state.Scanning = true;
            state.HasCompletedReview = false;
            bool entered = false;
            var timings = new ScanTimings();
            // Brings the awaits below back to Word's UI thread, where the document may be touched,
            // and nowhere once the add-in shuts down. A click in the pane arrives under WPF's context.
            if (SynchronizationContext.Current != UiContext)
                SynchronizationContext.SetSynchronizationContext(UiContext);
            try
            {
                state.Pane.Visible = true;
                state.Control.ShowProgress(Stage1);
                await scanGate.WaitAsync();
                entered = true;
                await ShowProgress(state, timings, ModelLoader.Classifier.IsCompleted ? Stage1 : "טוען את המודל…");
                NameClassifier classifier;
                using (timings.Measure("load_model"))
                    classifier = await ModelLoader.Classifier;

                string text;
                using (timings.Measure("read_text"))
                    text = DocumentScanner.ReadText(doc);
                timings.TextLength = text.Length;
                var progress = new Progress<(int Done, int Total)>(p =>
                {
                    using (timings.Measure("progress_ui"))
                        state.Control.ShowProgress(Stage1, p.Done, p.Total);
                });
                List<ScoredCandidate> scored;
                using (timings.Measure("score"))
                    scored = await Task.Run(() => DocumentScanner.Score(classifier, text, progress));
                timings.Candidates = scored.Count;

                // From here on the scan edits the document, pausing only for the pane to draw.
                using (new InputLock(doc))
                {
                    await ShowProgress(state, timings, Stage2);
                    // The spans are offsets into the text as it was scored.
                    bool changed;
                    using (timings.Measure("reread_text"))
                        changed = DocumentScanner.ReadText(doc) != text;
                    if (changed)
                    {
                        timings.Outcome = "document changed";
                        state.Control.ShowMessage("המסמך השתנה במהלך הסריקה. יש לסרוק שוב.");
                        return;
                    }
                    Thresholds thresholds = Thresholds.Load(out string problem);
                    await ApplyResults(state, doc, text, scored, thresholds, timings);
                    string shown = string.Join("\n", new[] { problem, notice, updateNotice }.Where(s => s != null));
                    using (timings.Measure("pane"))
                        state.Control.ShowSearchResults(state.ResultsList, shown.Length == 0 ? null : shown);
                    state.HasCompletedReview = true;
                }
            }
            catch (Exception ex)
            {
                timings.Outcome = "failed: " + ex.Message;
                state.Control.ShowMessage("הסריקה נכשלה: " + ex.Message);
            }
            finally
            {
                if (entered) scanGate.Release();
                state.Scanning = false;
                QueueReconciliation(doc, true);
                RemoveClosedWindows();
                System.Diagnostics.Debug.WriteLine("Genizah scan: " + timings.ToJson());
                finished?.Invoke(timings);
            }
        }

        // A scan's stages, as the pane names them.
        private const string Stage1 = "שלב 1 מתוך 3: סורק את השמות במסמך…";
        private const string Stage2 = "שלב 2 מתוך 3: מסמן את השמות במסמך…";
        private const string Stage3 = "שלב 3 מתוך 3: מצנזר את השמות…";

        /// <summary>How long the Word-side stages run between letting the pane draw their progress.</summary>
        private static readonly TimeSpan RepaintEvery = TimeSpan.FromMilliseconds(50);

        /// <summary>Shows a stage in the pane, and lets it draw before the UI thread goes back to work.</summary>
        private async Task ShowProgress(WindowResults state, ScanTimings timings, string stage, int done = 0, int total = 0)
        {
            using (timings.Measure("progress_ui"))
            {
                state.Control.ShowProgress(stage, done, total);
                await Repaint();
            }
        }

        /// <summary>
        /// Lets WPF lay out and draw the pane: the continuation runs below render priority. Input
        /// to the document is off meanwhile (<see cref="InputLock"/>).
        /// </summary>
        private static DispatcherPriorityAwaitable Repaint() =>
            Dispatcher.Yield(DispatcherPriority.Background);

        /// <summary>
        /// Marks each scored span in the document, and censors those the model is sure of. It runs
        /// on the UI thread, pausing every <see cref="RepaintEvery"/> for the pane to show how far
        /// it is; the caller holds an <see cref="InputLock"/> on the document.
        /// </summary>
        private async Task ApplyResults(WindowResults state, Word.Document doc, string text, List<ScoredCandidate> scored, Thresholds thresholds, ScanTimings timings)
        {
            Application.UndoRecord.StartCustomRecord("צנזור שמות לגניזה");
            Application.ScreenUpdating = false;
            try
            {
                using (timings.Measure("clear"))
                    ClearResults(state);
                PositionMap map;
                using (timings.Measure("build_map"))
                    map = PositionMap.Build(doc, text);
                // Paragraphs that don't add up to the text: the whole document's Characters, which is slow but sure.
                Word.Characters chars = map == null ? DocumentScanner.Plain(doc.Content).Characters : null;
                if (map == null)
                    timings.Outcome = "ok, without the position map";
                var sinceRepaint = System.Diagnostics.Stopwatch.StartNew();
                // Keep live ranges before any text changes: ranges move with edits.
                foreach (var s in scored)
                {
                    if (sinceRepaint.Elapsed >= RepaintEvery)
                    {
                        await ShowProgress(state, timings, Stage2, state.ResultsList.Count, scored.Count);
                        sinceRepaint.Restart();
                    }
                    Word.Range range;
                    using (timings.Measure("to_range"))
                        range = map != null ? map.ToRange(s.RawStart, s.RawEnd) : DocumentScanner.ToRange(doc, chars, s.RawStart, s.RawEnd);
                    Verdict verdict = thresholds == null ? Verdict.Ask : thresholds.Decide(s.Candidate.NameId, s.Logit);
                    // Never rewrite text other than what was scored: ask instead.
                    if (verdict == Verdict.Censor && WordText.ToUnicode(range.Text) != text.Substring(s.RawStart, s.RawEnd - s.RawStart))
                        verdict = Verdict.Ask;
                    DocumentScanner.Context(text, s.RawStart, s.RawEnd, 30, out string before, out string after);
                    SearchResult result;
                    using (timings.Measure("new_result"))
                        result = new SearchResult(doc, range, NameInfo.ById(s.Candidate.NameId), s.Logit,
                                                  thresholds?.Probability(s.Logit), verdict, before, after);
                    state.ResultsList.Add(result);
                    if (WordText.ToUnicode(result.OriginalText) != text.Substring(s.RawStart, s.RawEnd - s.RawStart))
                        timings.RangeMismatches++;
                }
                await ShowProgress(state, timings, Stage3, 0, state.ResultsList.Count);
                sinceRepaint.Restart();
                for (int i = 0; i < state.ResultsList.Count; i++)
                {
                    if (sinceRepaint.Elapsed >= RepaintEvery)
                    {
                        await ShowProgress(state, timings, Stage3, i, state.ResultsList.Count);
                        sinceRepaint.Restart();
                    }
                    var result = state.ResultsList[i];
                    using (timings.Measure("censor"))
                    {
                        if (result.Verdict == Verdict.Censor)
                            result.Censor();
                    }
                }
                await ShowProgress(state, timings, "מסיים…");
            }
            finally
            {
                Application.ScreenUpdating = true;
                Application.UndoRecord.EndCustomRecord();
            }
            using (timings.Measure("record_review"))
                RecordReview(state, true);
        }

        private void ClearResults(WindowResults state)
        {
            state.ResultsList.Clear();
            state.ReviewedText = null;
            state.ObservedText = null;
            state.ReviewInvalidated = false;
        }

        private void RecordReview(WindowResults state, bool freshScan = false)
        {
            try
            {
                if (freshScan) state.ReviewInvalidated = false;
                state.ObservedText = state.Document == null ? null : DocumentScanner.ReadText(state.Document);
                state.ReviewedText = state.ReviewInvalidated || state.ResultsList.Any(result => result.IsStale) ? null : state.ObservedText;
                QueueReconciliation(state.Document);
            }
            catch (Exception) { state.ReviewedText = null; }
        }

        /// <summary>True if the pane's results are for this document, and it hasn't changed since.</summary>
        private bool IsReviewed(WindowResults state, Word.Document doc)
        {
            return state.Document != null && state.ReviewedText != null
                && !state.ReviewInvalidated && !state.ResultsList.Any(result => result.IsStale)
                && state.Document == doc
                && DocumentScanner.ReadText(doc) == state.ReviewedText;
        }

#if DEBUG
        internal void RequestPaneScan(int hwnd) => windows[hwnd].Control.RequestScan();
        internal string InspectReviewUi(int hwnd) => windows[hwnd].Control.InspectReviewUi();
        private Func<PrintReviewStatus, int, PrintReviewChoice> printChoiceOverride;
        private PrintReviewStatus lastPrintStatus;
        private bool probingPrint;

        internal string ProbeBeforePrint(int hwnd, int choice)
        {
            foreach (Word.Window window in Application.Windows)
                if (window.Hwnd == hwnd)
                {
                    bool cancelled = false;
                    probingPrint = true;
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    printChoiceOverride = choice < 0 ? null : new Func<PrintReviewStatus, int, PrintReviewChoice>((status, pending) => (PrintReviewChoice)choice);
                    try { Application_DocumentBeforePrint(window.Document, ref cancelled); }
                    finally { printChoiceOverride = null; probingPrint = false; }
                    return "{\"status\":\"" + lastPrintStatus + "\",\"cancelled\":" + cancelled.ToString().ToLowerInvariant()
                        + ",\"elapsed_ms\":" + watch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "}";
                }
            throw new ArgumentException("Window not found");
        }

        internal void SeedReview(int hwnd, int start, int end, bool leave)
        {
            foreach (Word.Window window in Application.Windows)
                if (window.Hwnd == hwnd)
                {
                    var state = GetResults(window);
                    ClearResults(state);
                    state.ResultsList.Add(new SearchResult(window.Document, window.Document.Range(start, end),
                        NameInfo.ById("havaya"), 0, null, leave ? Verdict.Leave : Verdict.Ask, "", ""));
                    state.Pane.Visible = true;
                    state.Control.ShowSearchResults(state.ResultsList, null);
                    state.HasCompletedReview = true;
                    RecordReview(state, true);
                    return;
                }
            throw new ArgumentException("Window not found");
        }

        internal bool ReviewResult(int hwnd, int index, bool censor) => windows[hwnd].Control.ReviewResult(index, censor);

        internal string InspectResultRanges(int hwnd)
        {
            var state = windows[hwnd];
            Func<string, string> encode = text => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text ?? ""));
            return string.Join("\n", state.ResultsList.Select((result, index) => string.Join("\t",
                index, result.Range.Start, result.Range.End, encode(result.Range.Text),
                encode(result.OriginalText), encode(result.CensoredText), result.State, result.IsStale)));
        }

        internal string InspectWindowResults()
        {
            RemoveClosedWindows();
            return string.Join("\n", windows.Select(pair => string.Join("|",
                pair.Key, ((Word.Window)pair.Value.Pane.Window).Hwnd,
                pair.Value.Pane.Visible, pair.Value.ResultsList.Count,
                pair.Value.ResultsList.Count(result => result.State == ResultState.Pending),
                IsReviewed(pair.Value, pair.Value.Document), pair.Value.Scanning)));
        }

        internal void SetWindowPaneVisible(int hwnd, bool visible) => windows[hwnd].Pane.Visible = visible;

        /// <summary>What Application.COMAddIns("Genizah").Object returns: tools/benchmark's way in.</summary>
        protected override object RequestComAddInAutomationService() => new BenchmarkHook();
#endif

        #region VSTO generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(ThisAddIn_Startup);
            this.Shutdown += new System.EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}
