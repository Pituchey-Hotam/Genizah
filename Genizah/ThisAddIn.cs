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
        public List<SearchResult> ResultsList = new List<SearchResult>();
        public ResultsControl resultsControl = new ResultsControl();
        public CustomTaskPane resultsPane;

        private const string Title = "צנזור גניזה";

        /// <summary>The document ResultsList belongs to, and its text as of the last change made from the pane.</summary>
        private Word.Document resultsDocument;
        private string reviewedText;
        private bool scanning;

        /// <summary>Set once an update is installed for the next start: shown above the session's scan results.</summary>
        private string updateNotice;

        private void ThisAddIn_Startup(object sender, EventArgs e)
        {
            resultsPane = this.CustomTaskPanes.Add(resultsControl, "תוצאות");
            resultsPane.VisibleChanged += HandlePaneVisibleChanged;
            resultsControl.ResultChanged += (s, args) => RecordReview();
            Application.DocumentBeforePrint += new Word.ApplicationEvents4_DocumentBeforePrintEventHandler(Application_DocumentBeforePrint);
            // Off the UI thread, so the first scan doesn't wait for it.
            ModelLoader.StartLoading();
            var ui = new WindowsFormsSynchronizationContext();
            _ = Updater.CheckAsync(version => ui.Post(_ => AnnounceUpdate(version), null));
        }

        /// <summary>Says so quietly: on Word's status bar now, and above the results of the next scan.</summary>
        private void AnnounceUpdate(string version)
        {
            updateNotice = $"גרסה {version} של גניזה הותקנה, ותפעל מההפעלה הבאה של Word.";
            try { Application.StatusBar = updateNotice; }
            catch (Exception) { }
        }

        private void ThisAddIn_Shutdown(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// Handles a print event, warning the user of any names and offering to censor
        /// </summary>
        void Application_DocumentBeforePrint(Word.Document doc, ref bool cancel)
        {
            if (scanning)
            {
                MessageBox.Show("סריקת השמות עדיין פועלת. יש להדפיס שוב בסיומה.", Title);
                cancel = true;
                return;
            }
            try
            {
                if (IsReviewed(doc))
                {
                    // Scanned, and unchanged since: the pane's results stand.
                    int pending = ResultsList.Count(r => r.State == ResultState.Pending);
                    if (pending > 0 && MessageBox.Show($"{pending} שמות במסמך עדיין ממתינים להכרעה בחלונית התוצאות. להדפיס בכל זאת?",
                                                       Title, MessageBoxButtons.YesNo) == DialogResult.No)
                    {
                        cancel = true;
                        return;
                    }
                }
                else if (ContainsNames(doc))
                {
                    DialogResult dialogResult = MessageBox.Show("מסמך זה מכיל שמות שאינם נמחקים וטעון גניזה. האם תרצה לצנזר שמות ה' לפני הדפסה?\n" +
                                                                "ההדפסה תבוטל כדי שתוכל לעבור על התוצאות, ואחר כך יש להדפיס שוב.",
                                                                Title,
                                                                MessageBoxButtons.YesNoCancel);
                    if (dialogResult == DialogResult.Yes)
                    {
                        cancel = true;
                        CensorNames(doc, "לאחר ההכרעה בשמות יש להדפיס שוב.");
                        return;
                    }
                    else if (dialogResult == DialogResult.Cancel)
                    {
                        cancel = true;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                if (MessageBox.Show("לא ניתן היה לבדוק את המסמך לשמות: " + ex.Message + "\nלהדפיס בכל זאת?",
                                    Title, MessageBoxButtons.YesNo) == DialogResult.No)
                {
                    cancel = true;
                    return;
                }
            }

            // Remove Results HighLights
            foreach (var result in ResultsList)
            {
                try { result.Unhighlight(); }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// True if the document has a name to censor or ask about. Runs the classifier on the UI
        /// thread, stopping at the first such name; a print can't wait for an async answer.
        /// </summary>
        public bool ContainsNames(Word.Document doc)
        {
            // A download can take minutes, and the print would wait on Word's UI thread meanwhile.
            if (!ModelLoader.Classifier.IsCompleted && ModelLoader.Downloading)
                throw new InvalidOperationException("המודל עדיין בהורדה.");
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                NameClassifier classifier = ModelLoader.Classifier.GetAwaiter().GetResult();
                return DocumentScanner.HasNamesToCensor(classifier, DocumentScanner.ReadText(doc), Thresholds.Load(out _));
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        /// <summary>
        /// Scores every candidate name in the document (off the UI thread), censors those the
        /// model is sure of, and lists them all in the results pane.
        /// </summary>
        /// <param name="notice">Shown above the results, if given.</param>
        /// <param name="finished">Called with where the scan's time went, however it ended.</param>
        public async void CensorNames(Word.Document doc, string notice = null, Action<ScanTimings> finished = null)
        {
            if (scanning) return;
            scanning = true;
            var timings = new ScanTimings();
            // Brings the awaits below back to Word's UI thread, where the document may be touched.
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            try
            {
                resultsPane.Visible = true;
                await ShowProgress(timings, ModelLoader.Classifier.IsCompleted ? Stage1 : "טוען את המודל…");
                using (timings.Measure("clear"))
                    ClearResults();
                NameClassifier classifier;
                using (timings.Measure("load_model"))
                    classifier = await LoadClassifier();

                string text;
                using (timings.Measure("read_text"))
                    text = DocumentScanner.ReadText(doc);
                timings.TextLength = text.Length;
                var progress = new Progress<(int Done, int Total)>(p =>
                {
                    using (timings.Measure("progress_ui"))
                        resultsControl.ShowProgress(Stage1, p.Done, p.Total);
                });
                List<ScoredCandidate> scored;
                using (timings.Measure("score"))
                    scored = await Task.Run(() => DocumentScanner.Score(classifier, text, progress));
                timings.Candidates = scored.Count;

                // From here on the scan edits the document, pausing only for the pane to draw.
                using (new InputLock(doc))
                {
                    await ShowProgress(timings, Stage2);
                    // The spans are offsets into the text as it was scored.
                    bool changed;
                    using (timings.Measure("reread_text"))
                        changed = DocumentScanner.ReadText(doc) != text;
                    if (changed)
                    {
                        timings.Outcome = "document changed";
                        resultsControl.ShowMessage("המסמך השתנה במהלך הסריקה. יש לסרוק שוב.");
                        return;
                    }
                    Thresholds thresholds = Thresholds.Load(out string problem);
                    await ApplyResults(doc, text, scored, thresholds, timings);
                    string shown = string.Join("\n", new[] { problem, notice, updateNotice }.Where(s => s != null));
                    using (timings.Measure("pane"))
                        resultsControl.ShowSearchResults(ResultsList, shown.Length == 0 ? null : shown);
                }
            }
            catch (Exception ex)
            {
                timings.Outcome = "failed: " + ex.Message;
                resultsControl.ShowMessage("הסריקה נכשלה: " + ex.Message);
            }
            finally
            {
                scanning = false;
                System.Diagnostics.Debug.WriteLine("Genizah scan: " + timings.ToJson());
                finished?.Invoke(timings);
            }
        }

        /// <summary>
        /// The session's classifier, showing the graph's download in the pane if it has to wait
        /// for one: the first scan after an install without the graph, say.
        /// </summary>
        private async Task<NameClassifier> LoadClassifier()
        {
            Task<NameClassifier> loading = ModelLoader.Classifier;
            if (loading.IsCompleted)
                return await loading;
            // Created here, on the UI thread, so its reports come back to it.
            var progress = new Progress<(long Done, long Total)>(p =>
                resultsControl.ShowProgress(DownloadStage, (int)(p.Done >> 20), p.Total > 0 ? (int)(p.Total >> 20) : 0));
            Action<long, long> report = (done, total) => ((IProgress<(long, long)>)progress).Report((done, total));
            ModelLoader.DownloadProgress += report;
            try
            {
                return await loading;
            }
            finally
            {
                ModelLoader.DownloadProgress -= report;
            }
        }

        private const string DownloadStage = "מוריד את המודל (פעם אחת, כ־100 MB)…";

        // A scan's stages, as the pane names them.
        private const string Stage1 = "שלב 1 מתוך 3: סורק את השמות במסמך…";
        private const string Stage2 = "שלב 2 מתוך 3: מסמן את השמות במסמך…";
        private const string Stage3 = "שלב 3 מתוך 3: מצנזר ומדגיש את השמות…";

        /// <summary>How long the Word-side stages run between letting the pane draw their progress.</summary>
        private static readonly TimeSpan RepaintEvery = TimeSpan.FromMilliseconds(50);

        /// <summary>Shows a stage in the pane, and lets it draw before the UI thread goes back to work.</summary>
        private async Task ShowProgress(ScanTimings timings, string stage, int done = 0, int total = 0)
        {
            using (timings.Measure("progress_ui"))
            {
                resultsControl.ShowProgress(stage, done, total);
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
        private async Task ApplyResults(Word.Document doc, string text, List<ScoredCandidate> scored, Thresholds thresholds, ScanTimings timings)
        {
            Application.UndoRecord.StartCustomRecord("צנזור שמות לגניזה");
            Application.ScreenUpdating = false;
            try
            {
                PositionMap map;
                using (timings.Measure("build_map"))
                    map = PositionMap.Build(doc, text);
                // Paragraphs that don't add up to the text: the whole document's Characters, which is slow but sure.
                Word.Characters chars = map == null ? doc.Content.Characters : null;
                if (map == null)
                    timings.Outcome = "ok, without the position map";
                var sinceRepaint = System.Diagnostics.Stopwatch.StartNew();
                // Every span is bookmarked before any text changes: bookmarks move with the edits.
                foreach (var s in scored)
                {
                    if (sinceRepaint.Elapsed >= RepaintEvery)
                    {
                        await ShowProgress(timings, Stage2, ResultsList.Count, scored.Count);
                        sinceRepaint.Restart();
                    }
                    Word.Range range;
                    using (timings.Measure("to_range"))
                        range = map != null ? map.ToRange(s.RawStart, s.RawEnd) : DocumentScanner.ToRange(doc, chars, s.RawStart, s.RawEnd);
                    Verdict verdict = thresholds == null ? Verdict.Ask : thresholds.Decide(s.Candidate.NameId, s.Logit);
                    // Never rewrite text other than what was scored: ask instead.
                    if (verdict == Verdict.Censor && range.Text != text.Substring(s.RawStart, s.RawEnd - s.RawStart))
                        verdict = Verdict.Ask;
                    DocumentScanner.Context(text, s.RawStart, s.RawEnd, 30, out string before, out string after);
                    SearchResult result;
                    using (timings.Measure("new_result"))
                        result = new SearchResult(doc, range, NameInfo.ById(s.Candidate.NameId), s.Logit,
                                                  thresholds?.Probability(s.Logit), verdict, before, after);
                    ResultsList.Add(result);
                    if (result.OriginalText != text.Substring(s.RawStart, s.RawEnd - s.RawStart))
                        timings.RangeMismatches++;
                }
                await ShowProgress(timings, Stage3, 0, ResultsList.Count);
                sinceRepaint.Restart();
                for (int i = 0; i < ResultsList.Count; i++)
                {
                    if (sinceRepaint.Elapsed >= RepaintEvery)
                    {
                        await ShowProgress(timings, Stage3, i, ResultsList.Count);
                        sinceRepaint.Restart();
                    }
                    var result = ResultsList[i];
                    using (timings.Measure("censor_highlight"))
                    {
                        if (result.Verdict == Verdict.Censor)
                            result.Censor();
                        if (resultsPane.Visible)
                            result.Highlight();
                    }
                }
                await ShowProgress(timings, "מסיים…");
            }
            finally
            {
                Application.ScreenUpdating = true;
                Application.UndoRecord.EndCustomRecord();
            }
            resultsDocument = doc;
            using (timings.Measure("record_review"))
                RecordReview();
        }

        private void ClearResults()
        {
            foreach (var result in ResultsList)
            {
                // Its document may have been closed.
                try { result.Release(); }
                catch (Exception) { }
            }
            ResultsList.Clear();
            resultsDocument = null;
            reviewedText = null;
        }

        private void RecordReview()
        {
            try { reviewedText = resultsDocument == null ? null : DocumentScanner.ReadText(resultsDocument); }
            catch (Exception) { reviewedText = null; }
        }

        /// <summary>True if the pane's results are for this document, and it hasn't changed since.</summary>
        private bool IsReviewed(Word.Document doc)
        {
            return resultsDocument != null && reviewedText != null
                && resultsDocument.FullName == doc.FullName
                && DocumentScanner.ReadText(doc) == reviewedText;
        }

        private void HandlePaneVisibleChanged(object sender, EventArgs e)
        {
            foreach (var result in this.ResultsList)
            {
                try
                {
                    if (this.resultsPane.Visible)
                        result.Highlight();
                    else
                        result.Unhighlight();
                }
                catch (Exception) { }
            }
        }

#if DEBUG
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
