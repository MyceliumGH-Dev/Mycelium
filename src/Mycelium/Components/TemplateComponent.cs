using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Mycelium.Components
{
    /// <summary>
    /// Lists Mycelium template definitions (.gh/.ghx) and inserts one into the current
    /// document via the right-click menu. Templates ship in the plugin's Templates folder;
    /// additional folders can be supplied through the Directory input.
    /// </summary>
    public class TemplateComponent : GH_Component
    {
        private List<string> _folders = new List<string>();
        private List<List<string>> _filesPerFolder = new List<List<string>>();

        public TemplateComponent()
          : base("Mycelium Templates", "Templates",
              "Mycelium template files for quick starting",
              "Mycelium", "Utilities")
        {
        }

        // GUID predates the Mycelium rename; existing Grasshopper files depend on it.
        public override Guid ComponentGuid => new Guid("A1B2C3D4-5678-9ABC-DEF0-123456789ABC");

        protected override Bitmap Icon => ComponentIcons.Get("MyceliumTemplate");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Directory", "Dir", "Additional folder path(s) to search for Mycelium templates.", GH_ParamAccess.list);
            pManager[0].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Templates", "T", "Mycelium templates found in the search folders.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _folders = new List<string>();
            _filesPerFolder = new List<List<string>>();

            // Default search folder: Templates next to the plugin assembly
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var dirs = new List<string>
            {
                Path.Combine(pluginDir, "Templates")
            };

            var additionalDirs = new List<string>();
            DA.GetDataList(0, additionalDirs);
            dirs.AddRange(additionalDirs);

            foreach (var dir in dirs.Where(Directory.Exists))
            {
                var files = Directory.GetFiles(dir, "*.gh*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".gh", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".ghx", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (files.Any())
                {
                    _folders.Add(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    _filesPerFolder.Add(files);
                }
            }

            DA.SetDataList(0, _filesPerFolder.SelectMany(f => f));
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            menu.Items.Clear();

            if (_filesPerFolder.Count == 0)
            {
                Menu_AppendItem(menu, "No templates found", null, false);
                return;
            }

            for (int i = 0; i < _filesPerFolder.Count; i++)
                menu.Items.Add(BuildFolderMenu(_folders[i], _filesPerFolder[i]));
        }

        private ToolStripMenuItem BuildFolderMenu(string rootFolder, List<string> files)
        {
            var folderItem = new ToolStripMenuItem(new DirectoryInfo(rootFolder).Name);

            foreach (var file in files)
            {
                var fileDir = Path.GetDirectoryName(file);
                var name = Path.GetFileNameWithoutExtension(file);

                // Show the subfolder path for templates in nested folders
                var showName = fileDir.Length > rootFolder.Length
                    ? fileDir.Substring(rootFolder.Length + 1) + Path.DirectorySeparatorChar + name
                    : name;

                EventHandler onClick = (sender, e) =>
                {
                    var item = sender as ToolStripDropDownItem;
                    InsertTemplate(item.Tag.ToString());
                    ExpireSolution(true);
                };

                Menu_AppendItem(folderItem.DropDown, showName, onClick, null, file);
            }

            return folderItem;
        }

        /// <summary>
        /// Loads a template file and merges its objects into the active document,
        /// placed next to this component.
        /// </summary>
        private void InsertTemplate(string filePath)
        {
            var canvas = Grasshopper.Instances.ActiveCanvas;
            if (canvas == null || !canvas.Focused || !File.Exists(filePath))
                return;

            var io = new GH_DocumentIO();
            if (!io.Open(filePath))
            {
                MessageBox.Show("Failed to load template.");
                return;
            }

            var templateDoc = io.Document;

            templateDoc.SelectAll();
            // New object ids avoid conflicts with anything already on the canvas
            templateDoc.MutateAllIds();

            var box = templateDoc.BoundingBox(false);
            templateDoc.TranslateObjects(GetInsertOffset(box.Location), true);
            templateDoc.ExpireSolution();

            var currentDoc = canvas.Document;
            currentDoc.DeselectAll();
            currentDoc.MergeDocument(templateDoc);

            templateDoc.SelectAll();
        }

        /// <summary>
        /// Offset that places inserted template objects just left of and below this component.
        /// </summary>
        private Size GetInsertOffset(PointF fromLocation)
        {
            var moveX = Attributes.Bounds.Left - 80 - fromLocation.X;
            var moveY = Attributes.Bounds.Y + 180 - fromLocation.Y;
            return new Size(new Point(Convert.ToInt32(moveX), Convert.ToInt32(moveY)));
        }

        public override void CreateAttributes()
        {
            m_attributes = new TemplateComponentAttributes(this);
        }
    }

    /// <summary>
    /// Custom attributes that draw a "Right click" hint capsule below the component.
    /// </summary>
    public class TemplateComponentAttributes : GH_ComponentAttributes
    {
        public TemplateComponentAttributes(GH_Component owner) : base(owner)
        {
        }

        protected override void Layout()
        {
            base.Layout();

            // Reserve space below the component for the hint capsule
            var bounds = Bounds;
            bounds.Height += 20;
            Bounds = bounds;
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            var hintBounds = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 18);

            var capsule = GH_Capsule.CreateCapsule(hintBounds, GH_Palette.Black);
            capsule.Render(graphics, Selected, Owner.Locked, false);
            capsule.Dispose();

            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString("Right click", GH_FontServer.Small, Brushes.White, hintBounds, format);
        }
    }
}
