using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace FormFlux
{
    public class FluxTemplateComponent : GH_Component
    {
        private List<string> folderList = new List<string>();
        private List<List<string>> filesList = new List<List<string>>();

        public FluxTemplateComponent()
          : base("Flux Template", "FluxTemplate",
              "FormFlux template files for quick starting",
              "FormFlux", "Main")
        {
        }

        public override Guid ComponentGuid => new Guid("A1B2C3D4-5678-9ABC-DEF0-123456789ABC");

        protected override Bitmap Icon => Properties.Resources.template_icon;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Directory", "Dir", "Additional folder path to import FormFlux templates.", GH_ParamAccess.list);
            pManager[0].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Templates", "T", "FormFlux templates found in folders.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.folderList = new List<string>();
            this.filesList = new List<List<string>>();

            // Get plugin directory and add default Templates folder
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var dirs = new List<string>
            {
                Path.Combine(pluginDir, "Templates")
            };

            // Add any additional directories from input
            var additionalDirs = new List<string>();
            DA.GetDataList(0, additionalDirs);
            dirs.AddRange(additionalDirs);

            // Filter to only existing directories
            dirs = dirs.Where(d => Directory.Exists(d)).ToList();

            // Scan each directory for template files
            foreach (var dir in dirs)
            {
                var fs = Directory.GetFiles(dir, "*.gh*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".gh") || f.EndsWith(".ghx"))
                    .ToList();
                
                if (fs.Any())
                {
                    this.folderList.Add(Path.GetDirectoryName(Path.Combine(dir, "test.txt")));
                    this.filesList.Add(fs);
                }
            }

            DA.SetDataList(0, this.filesList.SelectMany(f => f));
        }

        private Size GetMoveVector(PointF FromLocation)
        {
            var moveX = this.Attributes.Bounds.Left - 80 - FromLocation.X;
            var moveY = this.Attributes.Bounds.Y + 180 - FromLocation.Y;
            var loc = new Point(Convert.ToInt32(moveX), Convert.ToInt32(moveY));

            return new Size(loc);
        }

        private void CreateTemplateFromFile(string FilePath)
        {
            var canvasCurrent = Grasshopper.Instances.ActiveCanvas;
            var f = canvasCurrent.Focused;
            var isFileExist = File.Exists(FilePath);

            if (f && isFileExist)
            {
                var io = new GH_DocumentIO();
                var success = io.Open(FilePath);

                if (!success)
                {
                    MessageBox.Show("Failed to load template.");
                    return;
                }

                var docTemp = io.Document;

                // Select all objects in template
                docTemp.SelectAll();
                
                // Generate new IDs to avoid conflicts
                docTemp.MutateAllIds();

                // Move template to position near this component
                var box = docTemp.BoundingBox(false);
                var vec = GetMoveVector(box.Location);
                docTemp.TranslateObjects(vec, true);

                docTemp.ExpireSolution();

                // Merge template into current document
                var docCurrent = canvasCurrent.Document;
                docCurrent.DeselectAll();
                docCurrent.MergeDocument(docTemp);
                
                // Select the newly added objects
                docTemp.SelectAll();
            }
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            var newMenu = menu;
            newMenu.Items.Clear();

            if (this.filesList.Count == 0)
            {
                Menu_AppendItem(menu, "No templates found", null, false);
                return;
            }

            var count = 0;
            foreach (var filesPerFolder in this.filesList)
            {
                var menuItem = AddFromFolder(this.folderList[count], filesPerFolder);
                menu.Items.Add(menuItem);
                count++;
            }
        }

        private ToolStripMenuItem AddFromFolder(string rootFolder, List<string> filesPerFolder)
        {
            var folderName = new DirectoryInfo(rootFolder).Name;
            var t = new ToolStripMenuItem(folderName);

            foreach (var item in filesPerFolder)
            {
                var p = Path.GetDirectoryName(item);
                var name = Path.GetFileNameWithoutExtension(item);
                var showName = p.Length > rootFolder.Length ? p.Replace(rootFolder + "\\", "") + "\\" + name : name;

                EventHandler ev = (object sender, EventArgs e) =>
                {
                    var a = sender as ToolStripDropDownItem;
                    CreateTemplateFromFile(a.Tag.ToString());
                    this.ExpireSolution(true);
                };

                Menu_AppendItem(t.DropDown, showName, ev, null, item);
            }

            return t;
        }

        public override void CreateAttributes()
        {
            m_attributes = new FluxTemplateAttributes(this);
        }
    }

    public class FluxTemplateAttributes : GH_ComponentAttributes
    {
        public FluxTemplateAttributes(GH_Component owner) : base(owner)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            
            // Add extra space below for the message
            var bounds = Bounds;
            bounds.Height += 20;
            Bounds = bounds;
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel == GH_CanvasChannel.Objects)
            {
                // Draw "Right click" message below the component
                var palette = GH_Palette.Black;
                var capsule = GH_Capsule.CreateCapsule(new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 18), palette);
                capsule.Render(graphics, Selected, Owner.Locked, false);
                capsule.Dispose();

                // Draw text
                var textBounds = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 18);
                var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                
                graphics.DrawString("Right click", GH_FontServer.Small, Brushes.White, textBounds, format);
            }
        }
    }
}
