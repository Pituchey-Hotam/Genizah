#if DEBUG
using System;
using System.IO;
using System.Runtime.InteropServices;

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
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public class BenchmarkHook : IBenchmarkHook
    {
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
