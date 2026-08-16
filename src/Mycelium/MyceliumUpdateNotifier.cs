using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Eto.Forms;
using Grasshopper.Kernel;
using Mycelium.Core;

namespace Mycelium
{
    /// <summary>
    /// One-time "update available" notice on startup (surface B). On the first Grasshopper canvas
    /// the plugin asks Yak whether a newer Mycelium is published and, if so — and the user hasn't
    /// skipped that version — shows a dismissible dialog offering to open the Package Manager. The
    /// network check is throttled and cached by <see cref="MyceliumUpdateCheck"/>; every failure is
    /// silent and the notice never blocks Grasshopper load (it fires after a canvas exists, off the
    /// load path).
    ///
    /// Surface A is the badge on the Mycelium Templates component; both honour the same opt-outs.
    /// </summary>
    public class MyceliumUpdateNotifier : GH_AssemblyPriority
    {
        private static bool _shown;

        public override GH_LoadingInstruction PriorityLoad()
        {
            try
            {
                // A canvas may already exist (plugin reloaded into a running Grasshopper); otherwise
                // wait for the first one so the dialog has a parent window.
                if (Grasshopper.Instances.ActiveCanvas != null)
                    _ = NotifyIfUpdateAsync();
                else
                    Grasshopper.Instances.CanvasCreated += OnCanvasCreated;
            }
            catch
            {
                // Never let the notice break plugin loading.
            }

            return GH_LoadingInstruction.Proceed;
        }

        private void OnCanvasCreated(Grasshopper.GUI.Canvas.GH_Canvas canvas)
        {
            Grasshopper.Instances.CanvasCreated -= OnCanvasCreated; // once
            _ = NotifyIfUpdateAsync();
        }

        private static async Task NotifyIfUpdateAsync()
        {
            try
            {
                if (_shown) return;
                if (MyceliumUpdateCheck.IsNeverRemind()) return; // permanent opt-out
                var info = await MyceliumUpdateCheck.CheckAsync(MyceliumVersion.Current).ConfigureAwait(false);
                if (info == null || !info.Available) return;
                if (MyceliumUpdateCheck.IsSkipped(info.Latest)) return;

                _shown = true;
                Rhino.RhinoApp.InvokeOnUiThread((Action)delegate { ShowDialog(info); });
            }
            catch
            {
                // Background check: never surface errors.
            }
        }

        private static void ShowDialog(MyceliumUpdateCheck.UpdateInfo info)
        {
            try
            {
                var dialog = new Dialog
                {
                    Title = "Mycelium Update Available",
                    Resizable = false,
                    Padding = new Eto.Drawing.Padding(16)
                };

                var instDateStr = string.IsNullOrEmpty(info.InstalledDate) ? "" : $" ({info.InstalledDate})";
                var latestDateStr = string.IsNullOrEmpty(info.LatestDate) ? "" : $" ({info.LatestDate})";

                var message = new Label
                {
                    Text = "A new version of Mycelium is available.\n\n" +
                           $"Installed:\t{info.Installed}{instDateStr}\nLatest:\t{info.Latest}{latestDateStr}",
                    Wrap = WrapMode.Word
                };

                var update = new Button { Text = "Update Now" };
                var skip = new Button { Text = "Skip This Version" };
                var later = new Button { Text = "Remind Me Later" };
                var never = new Button { Text = "Never Remind Me Again" };

                update.Click += (_, _) => { OpenPackageManager(); dialog.Close(); };
                skip.Click += (_, _) => { MyceliumUpdateCheck.Skip(info.Latest); dialog.Close(); };
                later.Click += (_, _) => dialog.Close(); // nothing persisted — remind next launch
                never.Click += (_, _) => { MyceliumUpdateCheck.SetNeverRemind(); dialog.Close(); };

                dialog.DefaultButton = update;
                dialog.AbortButton = later;

                var buttons = new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Items = { update, skip, later, never }
                };
                dialog.Content = new StackLayout
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 14,
                    Items = { message, buttons }
                };

                dialog.ShowModal();
            }
            catch
            {
                // A failed dialog must not crash Grasshopper.
            }
        }

        /// <summary>Opens the Rhino Package Manager (falls back to the website) so the user can update.</summary>
        internal static void OpenPackageManager()
        {
            try { Rhino.RhinoApp.RunScript("_PackageManager", false); }
            catch
            {
                try { Process.Start(new ProcessStartInfo("https://mycelium-gh.netlify.app") { UseShellExecute = true }); }
                catch { /* ignore */ }
            }
        }
    }
}
