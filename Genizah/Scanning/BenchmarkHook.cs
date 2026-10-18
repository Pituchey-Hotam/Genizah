#if DEBUG
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Diagnostics;
using Word = Microsoft.Office.Interop.Word;

namespace Genizah.Scanning
{
    /// <summary>
    /// Lets tools/benchmark start a scan from outside Word, through
    /// Application.COMAddIns("Genizah").Object. Debug builds only.
    /// </summary>
    [ComVisible(true)]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IBenchmarkHook
    {
        /// <summary>
        /// Scans the active document, as the ribbon button does, and writes the scan's timings
        /// (<see cref="ScanTimings.ToJson"/>) to <paramref name="timingsPath"/> when it ends.
        /// Returns at once: the caller waits for the file, since a call into Word would block
        /// while the scan holds its UI thread.
        /// </summary>
        void StartScan(string timingsPath);

        void StartWindowScan(int hwnd, string timingsPath);
        void InspectWindows(string outputPath);
        void SetPaneVisible(int hwnd, bool visible, string outputPath);
        void ProbeUndoEvents(string command, int hwnd, string outputPath);
        void InspectResultRanges(int hwnd, string outputPath);
        void ReviewResult(int hwnd, int index, bool censor, string outputPath);
        void SeedReview(int hwnd, int start, int end, bool leave, string outputPath);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public class BenchmarkHook : IBenchmarkHook
    {
        private UndoEventProbe eventProbe;

        public void SeedReview(int hwnd, int start, int end, bool leave, string outputPath) => Globals.ThisAddIn.UiContext.Post(_ =>
        {
            try { Globals.ThisAddIn.SeedReview(hwnd, start, end, leave); WriteOutput(outputPath, "ok"); }
            catch (Exception ex) { WriteOutput(outputPath, "ERROR: " + ex); }
        }, null);

        public void ReviewResult(int hwnd, int index, bool censor, string outputPath) => Globals.ThisAddIn.UiContext.Post(_ =>
        {
            try { WriteOutput(outputPath, Globals.ThisAddIn.ReviewResult(hwnd, index, censor).ToString()); }
            catch (Exception ex) { WriteOutput(outputPath, "ERROR: " + ex); }
        }, null);

        public void InspectResultRanges(int hwnd, string outputPath) => Globals.ThisAddIn.UiContext.Post(_ =>
        {
            try { WriteOutput(outputPath, Globals.ThisAddIn.InspectResultRanges(hwnd)); }
            catch (Exception ex) { WriteOutput(outputPath, "ERROR: " + ex); }
        }, null);

        public void ProbeUndoEvents(string command, int hwnd, string outputPath) => Globals.ThisAddIn.UiContext.Post(_ =>
        {
            try
            {
                if (command == "start")
                {
                    eventProbe?.Dispose();
                    eventProbe = new UndoEventProbe(Globals.ThisAddIn.Application, hwnd);
                }
                else if (command == "clear") eventProbe.Clear();
                else if (command == "stop")
                {
                    eventProbe?.Dispose();
                    eventProbe = null;
                }
                WriteOutput(outputPath, eventProbe == null ? "stopped" : eventProbe.Capture());
            }
            catch (Exception ex) { WriteOutput(outputPath, "ERROR: " + ex); }
        }, null);

        // Opt-in, Debug-only instrumentation. No event hooks exist until the probe starts.
        private sealed class UndoEventProbe : IDisposable
        {
            private readonly Word.Application application;
            private readonly Word.Window window;
            private readonly List<string> events = new List<string>();
            private readonly Stopwatch clock = Stopwatch.StartNew();

            public UndoEventProbe(Word.Application application, int hwnd)
            {
                this.application = application;
                foreach (Word.Window candidate in application.Windows)
                    if (candidate.Hwnd == hwnd) { window = candidate; break; }
                if (window == null) throw new ArgumentException("Window not found");
                application.WindowSelectionChange += SelectionChanged;
                application.DocumentChange += DocumentChanged;
                application.WindowActivate += Activated;
                application.WindowDeactivate += Deactivated;
                application.WindowSize += Resized;
            }

            public void Clear() { events.Clear(); clock.Restart(); }
            private void Record(string name) => events.Add(clock.ElapsedMilliseconds + "\t" + name);
            private void SelectionChanged(Word.Selection selection) => Record("WindowSelectionChange");
            private void DocumentChanged() => Record("DocumentChange");
            private void Activated(Word.Document doc, Word.Window changed) => Record("WindowActivate");
            private void Deactivated(Word.Document doc, Word.Window changed) => Record("WindowDeactivate");
            private void Resized(Word.Document doc, Word.Window changed) => Record("WindowSize");

            public string Capture()
            {
                Word.Selection selection = window.Selection;
                return "selection\t" + selection.Start + "\t" + selection.End + "\n" + string.Join("\n", events);
            }

            public void Dispose()
            {
                application.WindowSelectionChange -= SelectionChanged;
                application.DocumentChange -= DocumentChanged;
                application.WindowActivate -= Activated;
                application.WindowDeactivate -= Deactivated;
                application.WindowSize -= Resized;
            }
        }

        public void StartWindowScan(int hwnd, string timingsPath)
        {
            Globals.ThisAddIn.UiContext.Post(_ =>
            {
                foreach (Microsoft.Office.Interop.Word.Window window in Globals.ThisAddIn.Application.Windows)
                    if (window.Hwnd == hwnd)
                    {
                        Globals.ThisAddIn.CensorNames(window.Document,
                            finished: timings => WriteOutput(timingsPath, timings.ToJson()), window: window);
                        return;
                    }
                WriteOutput(timingsPath, "Window not found");
            }, null);
        }

        // File replies keep RPC callers from blocking Word's UI thread while it is scanning.
        public void InspectWindows(string outputPath) => Globals.ThisAddIn.UiContext.Post(_ =>
            WriteOutput(outputPath, typeof(BenchmarkHook).Assembly.CodeBase + "\n" +
                Globals.ThisAddIn.InspectWindowResults()), null);

        public void SetPaneVisible(int hwnd, bool visible, string outputPath) => Globals.ThisAddIn.UiContext.Post(_ =>
        {
            Globals.ThisAddIn.SetWindowPaneVisible(hwnd, visible);
            WriteOutput(outputPath, "ok");
        }, null);

        private static void WriteOutput(string path, string contents)
        {
            File.WriteAllText(path + ".tmp", contents);
            File.Move(path + ".tmp", path);
        }

        public void StartScan(string timingsPath)
        {
            File.Delete(timingsPath);
            File.Delete(timingsPath + ".tmp");
            // A call from another process arrives on an RPC thread, not Word's UI thread, and the
            // scan's awaits need that thread's message loop: hand it over.
            Globals.ThisAddIn.UiContext.Post(_ =>
                Globals.ThisAddIn.CensorNames(Globals.ThisAddIn.Application.ActiveDocument,
                                              finished: t =>
                                              {
                                                  // Whole or not at all: the caller reads it as soon as it appears.
                                                  File.WriteAllText(timingsPath + ".tmp", t.ToJson());
                                                  File.Move(timingsPath + ".tmp", timingsPath);
                                              }), null);
        }
    }
}
#endif
