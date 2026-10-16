using System;
using System.Runtime.InteropServices;
using Word = Microsoft.Office.Interop.Word;

namespace Genizah.Scanning
{
    /// <summary>
    /// Turns off mouse and keyboard input to a document's Word window, as a modal dialog does.
    /// The scan's Word-side work lets the results pane redraw its progress between edits, and
    /// this keeps the user from editing the document in those gaps: the spans it maps are
    /// offsets into the text as it was scored. Dispose turns input back on and returns the
    /// keyboard focus to where it was.
    /// </summary>
    internal sealed class InputLock : IDisposable
    {
        private readonly IntPtr window;
        private readonly IntPtr focus;
        private readonly bool wasDisabled;

        public InputLock(Word.Document doc)
        {
            try
            {
                window = GetAncestor(new IntPtr(doc.ActiveWindow.Hwnd), GA_ROOT);
            }
            catch (COMException)
            {
                // A document with no window has nowhere to type.
                return;
            }
            focus = GetFocus();
            // Already disabled (a modal dialog is open): leave it to whoever disabled it.
            wasDisabled = EnableWindow(window, false);
        }

        public void Dispose()
        {
            if (window == IntPtr.Zero || wasDisabled) return;
            EnableWindow(window, true);
            if (focus != IntPtr.Zero && IsWindow(focus))
                SetFocus(focus);
        }

        private const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern bool EnableWindow(IntPtr hwnd, bool enable);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hwnd);
    }
}
