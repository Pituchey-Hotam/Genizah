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
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public class BenchmarkHook : IBenchmarkHook
    {
        public void StartScan(string timingsPath)
        {
            File.Delete(timingsPath);
            File.Delete(timingsPath + ".tmp");
            // A call from another process arrives on an RPC thread, not Word's UI thread, and the
            // scan's awaits need that thread's message loop: hand it over.
            Globals.ThisAddIn.resultsControl.BeginInvoke((Action)(() =>
                Globals.ThisAddIn.CensorNames(Globals.ThisAddIn.Application.ActiveDocument,
                                              finished: t =>
                                              {
                                                  // Whole or not at all: the caller reads it as soon as it appears.
                                                  File.WriteAllText(timingsPath + ".tmp", t.ToJson());
                                                  File.Move(timingsPath + ".tmp", timingsPath);
                                              })));
        }
    }
}
#endif
