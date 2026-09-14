using System.Threading;
using System.Windows.Forms;

namespace Mycelium.Components;

/// <summary>
/// Lifetime management for canvas-owned <see cref="ToolStripDropDown"/> menus — the ones an
/// attributes class constructs itself and shows on a click, as opposed to the menu Grasshopper
/// hands to AppendAdditionalMenuItems (which Grasshopper owns and disposes).
/// </summary>
public static class CanvasMenu
{
    /// <summary>
    /// Disposes <paramref name="menu"/> deterministically on the UI thread once it closes.
    ///
    /// Left undisposed, the menu becomes GC garbage and is finalized at an arbitrary later time
    /// on the .NET FINALIZER thread — where disposing its items triggers a layout pass
    /// (CalculateAutoSize -> OnLayout -> Control.GetBounds) that reaches into the native NSView
    /// Eto/AppKit already tore down when the dropdown closed. That throws ObjectDisposedException
    /// with nothing to catch it on the finalizer thread, which CoreCLR treats as fatal and aborts
    /// the whole process (SIGABRT in FinalizerThread::FinalizeAllObjects disposing a
    /// ToolStripDropDownMenu).
    ///
    /// Closed fires while WinForms' ModalMenuFilter is still unwinding the mouse message, so
    /// disposing synchronously there leaves that filter holding a disposed active menu; its
    /// subsequent CloseActiveDropDown call then throws ObjectDisposedException outside any plugin
    /// frame and terminates Rhino on Windows. Disposal is therefore posted to the next UI-message
    /// turn.
    /// </summary>
    public static T DisposeOnClose<T>(this T menu) where T : ToolStripDropDown
    {
        var uiContext = SynchronizationContext.Current;
        menu.Closed += (_, _) =>
        {
            if (uiContext != null)
                uiContext.Post(_ => menu.Dispose(), null);
            else
                menu.BeginInvoke((MethodInvoker)(() => menu.Dispose()));
        };
        return menu;
    }
}
