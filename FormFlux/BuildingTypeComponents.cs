using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace FormFlux
{
    public abstract class BuildingConfigComponent : GH_Component
    {
        protected int _typeIndex;
        protected string _typeName;
        protected string _nickName;
        protected string _description;

        public BuildingConfigComponent(string name, string nickname, string description, int typeIndex)
          : base(name, nickname, description, "FormFlux", "Building Types")
        {
            _typeIndex = typeIndex;
            _typeName = name;
            _nickName = nickname;
            _description = description;
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("MinFloors", "Fmin", "Minimum floors", GH_ParamAccess.item, 3.0);
            pManager.AddNumberParameter("MaxFloors", "Fmax", "Maximum floors", GH_ParamAccess.item, 6.0);
            pManager.AddNumberParameter("Radius", "R", "Corner radius for footprint", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("MinArea", "MinA", "Minimum footprint area (m²)", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("MinSetback", "Smin", "Minimum setback distance (m)", GH_ParamAccess.item, 3.0);
            pManager.AddNumberParameter("MaxSetback", "Smax", "Maximum setback distance (m)", GH_ParamAccess.item, 3.0);
            pManager.AddNumberParameter("MinDepth", "Dmin", "Minimum building depth/width (m)", GH_ParamAccess.item, 12.0);
            pManager.AddNumberParameter("MaxDepth", "Dmax", "Maximum building depth/width (m)", GH_ParamAccess.item, 12.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Config", "Cfg", "Building configuration data", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double minFloors = 3.0;
            double maxFloors = 6.0;
            double radius = 0.0;
            double minArea = 100.0;
            double minSetback = 3.0;
            double maxSetback = 3.0;
            double minDepth = 12.0;
            double maxDepth = 12.0;

            if (!DA.GetData(0, ref minFloors)) return;
            if (!DA.GetData(1, ref maxFloors)) return;
            DA.GetData(2, ref radius);
            DA.GetData(3, ref minArea);
            DA.GetData(4, ref minSetback);
            DA.GetData(5, ref maxSetback);
            DA.GetData(6, ref minDepth);
            DA.GetData(7, ref maxDepth);

            // Format: TypeIndex|MinFloors|MaxFloors|Radius|MinArea|MinSetback|MaxSetback|MinDepth|MaxDepth
            string config = $"{_typeIndex}|{minFloors}|{maxFloors}|{radius}|{minArea}|{minSetback}|{maxSetback}|{minDepth}|{maxDepth}";
            DA.SetData(0, config);
        }

        public override Guid ComponentGuid => Guid.NewGuid(); // Should be overridden
        protected override System.Drawing.Bitmap Icon => null; // TODO: Add icons
    }

    public class CourtyardConfig : BuildingConfigComponent
    {
        public CourtyardConfig() : base("Courtyard Config", "CrtCfg", "Configure Courtyard building parameters", 0) { }
        public override Guid ComponentGuid => new Guid("11111111-1111-1111-1111-111111111111");
        protected override System.Drawing.Bitmap Icon => Properties.Resources.FluxCourtyard;
    }

    public class LinearConfig : BuildingConfigComponent
    {
        public LinearConfig() : base("Linear Config", "LinCfg", "Configure Linear building parameters", 1) { }
        public override Guid ComponentGuid => new Guid("22222222-2222-2222-2222-222222222222");
        protected override System.Drawing.Bitmap Icon => Properties.Resources.FluxLinear;
    }

    public class PointConfig : BuildingConfigComponent
    {
        public PointConfig() : base("Point Config", "PntCfg", "Configure Point building parameters", 2) { }
        public override Guid ComponentGuid => new Guid("33333333-3333-3333-3333-333333333333");
        protected override System.Drawing.Bitmap Icon => Properties.Resources.FluxPoint;
    }

    public class LShapeConfig : BuildingConfigComponent
    {
        public LShapeConfig() : base("L-Shape Config", "LCfg", "Configure L-Shape building parameters", 3) { }
        public override Guid ComponentGuid => new Guid("44444444-4444-4444-4444-444444444444");
        protected override System.Drawing.Bitmap Icon => Properties.Resources.FluxL;
    }

    public class UShapeConfig : BuildingConfigComponent
    {
        public UShapeConfig() : base("U-Shape Config", "UCfg", "Configure U-Shape building parameters", 4) { }
        public override Guid ComponentGuid => new Guid("55555555-5555-5555-5555-555555555555");
        protected override System.Drawing.Bitmap Icon => Properties.Resources.FluxU;
    }

    public class TallBuildingConfig : BuildingConfigComponent
    {
        public TallBuildingConfig() : base("Tall Building Config", "TallCfg", "Configure Tall Building parameters", 5) { }
        public override Guid ComponentGuid => new Guid("66666666-6666-6666-6666-666666666666");
        protected override System.Drawing.Bitmap Icon => Properties.Resources.FluxTall;
    }
}
