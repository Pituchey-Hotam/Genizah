using System;
using System.Threading;
using System.Windows.Forms;

namespace Genizah
{
    /// <summary>
    /// Runs callbacks on Word's UI thread, as a WindowsFormsSynchronizationContext does, until the
    /// add-in shuts down, and drops them from then on. When Word quits, it unloads the add-in's
    /// AppDomain after Shutdown, and WinForms releases the domain's windows, the marshaling
    /// control's among them; a WindowsFormsSynchronizationContext's Post throws after that. A
    /// scan's awaits on the model load and the scoring complete on the thread pool and post from
    /// there, where an exception is unhandled: it would end Word, and Word then disables the
    /// add-in. Not a WindowsFormsSynchronizationContext itself, so WinForms neither replaces nor
    /// uninstalls it.
    /// </summary>
    internal sealed class UiSynchronizationContext : SynchronizationContext
    {
        // Created on the UI thread: its marshaling control belongs to the thread that creates it.
        private readonly WindowsFormsSynchronizationContext ui = new WindowsFormsSynchronizationContext();
        private volatile bool closed;

        /// <summary>Drops every callback from now on, those already posted included.</summary>
        public void Close() => closed = true;

        public override void Post(SendOrPostCallback d, object state)
        {
            if (closed) return;
            try
            {
                ui.Post(_ =>
                {
                    if (closed) return;
                    // The thread's own context doesn't last between calls into the add-in, and WinForms
                    // fills the gap with its own: the awaits in a callback must capture this one instead.
                    var previous = Current;
                    SetSynchronizationContext(this);
                    try { d(state); }
                    finally { SetSynchronizationContext(previous); }
                }, null);
            }
            catch (InvalidOperationException)
            {
                // The marshaling control's window is gone: the AppDomain is unloading.
            }
        }

        public override void Send(SendOrPostCallback d, object state) => ui.Send(d, state);

        public override SynchronizationContext CreateCopy() => this;
    }
}
