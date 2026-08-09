using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Mycelium.Core;
using Rhino.Geometry;

namespace Mycelium.Components
{
    public class GreenNetworkGeneratorComponent : GH_Component
    {
        public GreenNetworkGeneratorComponent() : base("Green Network Generator", "GreenNet",
            "Generates seeded perimeter belts, connecting corridors, refuge patches, and trees",
            "Mycelium", "Vegetation") { }

        public override Guid ComponentGuid => new Guid("E3AC4CB8-8CE6-4E8C-97EF-9C4BF29C827B");
        protected override Bitmap Icon => ComponentIcons.Get("MyceliumGreenNetwork");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddCurveParameter("Boundary", "B", "Closed planar site boundary", GH_ParamAccess.item);
            p.AddCurveParameter("CorridorGuides", "G", "Optional corridor axes; leave empty to connect park and refuge anchors automatically", GH_ParamAccess.list);
            p.AddCurveParameter("BuildingFootprints", "F", "Connect Massing Generator Footprints here; these areas are removed from the green network", GH_ParamAccess.list);
            p.AddCurveParameter("ExistingParks", "P", "Connect Massing Generator Parks here; they become refuge anchors in the connected network", GH_ParamAccess.list);
            p.AddNumberParameter("BeltWidth", "BW", "Inward perimeter-belt width", GH_ParamAccess.item, 8.0);
            p.AddNumberParameter("CorridorWidth", "CW", "Full corridor width", GH_ParamAccess.item, 5.0);
            p.AddIntegerParameter("RefugeCount", "RC", "Number of seeded refuge patches", GH_ParamAccess.item, 3);
            p.AddNumberParameter("RefugeRadius", "RR", "Refuge-patch radius", GH_ParamAccess.item, 8.0);
            p.AddNumberParameter("TreeDensity", "TD", "Schematic tree density (0-100); zero disables trees", GH_ParamAccess.item, 10.0);
            p.AddIntegerParameter("Seed", "S", "Random seed; identical inputs reproduce the same network", GH_ParamAccess.item, 0);
            p[1].Optional = true;
            p[2].Optional = true;
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("GreenRegions", "GR", "All generated green-region boundaries", GH_ParamAccess.list);
            p.AddCurveParameter("Belt", "B", "Perimeter-belt boundaries", GH_ParamAccess.list);
            p.AddCurveParameter("Corridors", "C", "Green-corridor boundaries", GH_ParamAccess.list);
            p.AddCurveParameter("Refuges", "R", "Refuge-patch boundaries", GH_ParamAccess.list);
            p.AddBrepParameter("Trees", "T", "Seeded schematic tree canopies", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Curve boundary = null; var guides = new List<Curve>(); var obstacles = new List<Curve>(); var parks = new List<Curve>();
            double belt = 8, corridor = 5, radius = 8, density = 10; int count = 3, seed = 0;
            if (!da.GetData(0, ref boundary)) return;
            da.GetDataList(1, guides); da.GetDataList(2, obstacles); da.GetDataList(3, parks);
            da.GetData(4, ref belt); da.GetData(5, ref corridor); da.GetData(6, ref count);
            da.GetData(7, ref radius); da.GetData(8, ref density); da.GetData(9, ref seed);
            try
            {
                var r = GreenNetworkGenerator.Generate(boundary, guides, obstacles, parks,
                    Math.Max(0, belt), Math.Max(0, corridor), Math.Max(0, count),
                    Math.Max(0.001, radius), Math.Max(0, Math.Min(100, density)), seed);
                da.SetDataList(0, r.AllRegions); da.SetDataList(1, r.Belt);
                da.SetDataList(2, r.Corridors); da.SetDataList(3, r.Refuges); da.SetDataList(4, r.Trees);
            }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); }
        }
    }
}
