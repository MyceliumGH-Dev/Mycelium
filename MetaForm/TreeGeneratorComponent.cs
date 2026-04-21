using System;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace MetaForm
{
    /// <summary>
    /// Tree configuration component that provides tree parameters to MetaForm
    /// </summary>
    public class TreeGeneratorComponent : GH_Component
    {
        public TreeGeneratorComponent()
          : base("Tree Config", "TreeCfg",
              "Configure tree generation parameters for MetaForm",
              "MetaForm", "Tree")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("TreeDensity", "TDens", "Tree density percentage (0-100%). 100% = maximum density (1 tree per 25m²)", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("MinDiameter", "MinD", "Minimum tree diameter in meters", GH_ParamAccess.item, 2.0);
            pManager.AddNumberParameter("MaxDiameter", "MaxD", "Maximum tree diameter in meters", GH_ParamAccess.item, 5.0);
            pManager.AddBooleanParameter("GenerateInCourtyards", "Court", "Generate trees in building courtyards", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Trees", "Trees", "Tree configuration data for MetaForm", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get inputs
            double treeDensity = 10.0;
            double minDiameter = 2.0;
            double maxDiameter = 5.0;
            bool generateInCourtyards = true;

            DA.GetData(0, ref treeDensity);
            DA.GetData(1, ref minDiameter);
            DA.GetData(2, ref maxDiameter);
            DA.GetData(3, ref generateInCourtyards);

            // Validate inputs
            treeDensity = Math.Max(0, Math.Min(100, treeDensity));
            minDiameter = Math.Max(0.1, minDiameter);
            maxDiameter = Math.Max(minDiameter, maxDiameter);

            // Create configuration string: density|minDiameter|maxDiameter|courtyards
            string config = $"{treeDensity:F2}|{minDiameter:F2}|{maxDiameter:F2}|{generateInCourtyards}";

            // Set output
            DA.SetData(0, config);
        }

        protected override Bitmap Icon => Properties.Resources.tree_icon;

        public override Guid ComponentGuid => new Guid("B7E8F3A2-4D6C-4E9F-8A1B-3C5D7E9F2A4B");
    }
}
