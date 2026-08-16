using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Mycelium.Components
{
    /// <summary>
    /// Custom attributes for the template component (adopted from Eddy3D): a clickable
    /// "Select Template" button below the component plus a source label line.
    /// </summary>
    public class TemplateComponentAttributes : GH_ComponentAttributes
    {
        private Rectangle ButtonBounds { get; set; }
        private Rectangle SourceBounds { get; set; }
        private bool _mouseOverButton;
        private bool _mouseOverSource;

        public TemplateComponentAttributes(GH_Component component) : base(component)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Rectangle rec0 = GH_Convert.ToRectangle(Bounds);
            rec0.Height += 36;

            Rectangle rec1 = rec0;
            rec1.Y = rec1.Bottom - 36;
            rec1.Height = 22;
            rec1.Inflate(-2, -2);

            Rectangle rec2 = rec0;
            rec2.Y = rec1.Bottom;
            rec2.Height = rec0.Bottom - rec1.Bottom;
            rec2.Inflate(-4, 0);

            Bounds = rec0;
            ButtonBounds = rec1;
            SourceBounds = rec2;
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            var comp = Owner as TemplateComponent;
            GH_Palette palette = Owner.Locked ? GH_Palette.Locked : GH_Palette.Black;

            string buttonText = "Select Template";
            if (comp != null)
            {
                if (comp.IsFetching) buttonText = "⌛ Syncing...";
                else if (comp.ErrorMessage != null) buttonText = "⚠ Sync Error";
                else if (comp.UpdateAvailable) buttonText = "✨ Update Templates";
            }

            GH_Capsule button = GH_Capsule.CreateTextCapsule(ButtonBounds, ButtonBounds, palette, buttonText, 2, 0);
            button.Render(graphics, Selected || (_mouseOverButton && !Owner.Locked), Owner.Locked, false);
            button.Dispose();

            if (comp != null)
            {
                // When a newer Mycelium is published, the source line becomes an amber "update
                // available" badge that opens the Package Manager on click (surface A); otherwise
                // it stays the template-source label.
                var showUpdate = comp.PluginUpdateAvailable;
                Color sourceColor = showUpdate ? Color.FromArgb(230, 150, 30)
                                  : Owner.Locked ? Color.FromArgb(120, 120, 120)
                                  : _mouseOverSource ? Color.FromArgb(40, 40, 40)
                                  : Color.FromArgb(130, 130, 130);
                var sourceText = showUpdate
                    ? $"⬆ Mycelium {comp.LatestPluginVersion} available"
                    : comp.TemplateSourceLabel;

                using (var brush = new SolidBrush(sourceColor))
                using (var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap,
                })
                {
                    graphics.DrawString(sourceText, GH_FontServer.Small, brush, SourceBounds, format);
                }
            }
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            bool needsRedraw = false;
            Point mouseLoc = Point.Round(e.CanvasLocation);

            bool isOverButton = !Owner.Locked && ButtonBounds.Contains(mouseLoc);
            if (isOverButton != _mouseOverButton)
            {
                _mouseOverButton = isOverButton;
                needsRedraw = true;
            }

            bool isOverSource = !Owner.Locked && SourceBounds.Contains(mouseLoc);
            if (isOverSource != _mouseOverSource)
            {
                _mouseOverSource = isOverSource;
                needsRedraw = true;
            }

            if (needsRedraw)
            {
                sender.Invalidate();
                return GH_ObjectResponse.Handled;
            }

            return base.RespondToMouseMove(sender, e);
        }

        public override bool IsTooltipRegion(PointF canvasLocation)
        {
            return ButtonBounds.Contains(Point.Round(canvasLocation))
                || SourceBounds.Contains(Point.Round(canvasLocation))
                || base.IsTooltipRegion(canvasLocation);
        }

        public override void SetupTooltip(PointF canvasLocation, GH_TooltipDisplayEventArgs e)
        {
            var comp = Owner as TemplateComponent;
            if (ButtonBounds.Contains(Point.Round(canvasLocation)))
            {
                if (comp != null && comp.IsFetching)
                {
                    e.Title = "Syncing Templates";
                    e.Text = "Downloading the latest template list from GitHub. Please wait...";
                }
                else if (comp != null && comp.ErrorMessage != null)
                {
                    e.Title = "Sync Error";
                    e.Text = comp.ErrorMessage + "\n\nRight-click to retry fetch.";
                }
                else if (comp != null && comp.UpdateAvailable)
                {
                    e.Title = "Update Templates";
                    e.Text = "A newer version of the templates is available on GitHub. Click to synchronize.";
                }
                else
                {
                    e.Title = "Select Template";
                    e.Text = "Click to browse and load example templates from GitHub, bundled files, or local folders.";
                }
                if (Owner.Locked) e.Text += "\n\n(Disabled)";
                e.Icon = Owner.Icon_24x24;
            }
            else if (SourceBounds.Contains(Point.Round(canvasLocation)) && comp != null && comp.PluginUpdateAvailable)
            {
                e.Title = "⬆ Mycelium Update Available";
                e.Text = $"Mycelium {comp.LatestPluginVersion} is available on the Rhino Package Manager.\n\n" +
                         "Click to open the Package Manager and update.";
                e.Icon = Owner.Icon_24x24;
            }
            else if (SourceBounds.Contains(Point.Round(canvasLocation)))
            {
                e.Title = "Template Source";
                e.Text = $"Templates are fetched from {(comp != null ? comp.TemplateSourceLabel : "GitHub")}.\n\n" +
                         $"Local cache: {comp?.MainRepoDir}\n\n" +
                         "Left-click to open the local cache folder.\n" +
                         "Right-click to view the repository on GitHub.";
                if (Owner.Locked) e.Text += "\n\n(Disabled)";
                e.Icon = Owner.Icon_24x24;
            }
            else
            {
                base.SetupTooltip(canvasLocation, e);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (Owner.Locked) return base.RespondToMouseDown(sender, e);

            Point mouseLoc = Point.Round(e.CanvasLocation);

            if (ButtonBounds.Contains(mouseLoc) && e.Button == MouseButtons.Left)
            {
                var menu = new ContextMenuStrip();
                if (Owner is TemplateComponent comp)
                {
                    comp.AppendTemplateMenuItems(menu);
                }
                menu.Show(sender, e.ControlLocation);
                return GH_ObjectResponse.Handled;
            }

            if (SourceBounds.Contains(mouseLoc))
            {
                if (Owner is TemplateComponent comp)
                {
                    // The badge takes over the source line when a plugin update is available.
                    if (e.Button == MouseButtons.Left && comp.PluginUpdateAvailable) comp.OpenPackageManager();
                    else if (e.Button == MouseButtons.Left) comp.OpenLocalTemplateFolder();
                    else if (e.Button == MouseButtons.Right) comp.OpenGitHubRepository();
                    return GH_ObjectResponse.Handled;
                }
            }

            return base.RespondToMouseDown(sender, e);
        }
    }
}
