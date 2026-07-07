using System;
using System.Drawing;
using Grasshopper.Kernel;
using Mycelium.Core;

namespace Mycelium.Components
{
    /// <summary>
    /// Base class for the per-typology config components. Each subclass exposes the same
    /// parameter set and emits a serialized <see cref="BuildingTypeConfig"/> for the
    /// massing generator.
    /// </summary>
    public abstract class BuildingConfigComponent : GH_Component
    {
        private readonly BuildingType _type;

        protected BuildingConfigComponent(string name, string nickname, string description, BuildingType type)
          : base(name, nickname, description, "Mycelium", "Building Types")
        {
            _type = type;
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
            var config = BuildingTypeConfig.Default;
            config.Type = _type;

            if (!DA.GetData(0, ref config.MinFloors)) return;
            if (!DA.GetData(1, ref config.MaxFloors)) return;
            DA.GetData(2, ref config.CornerRadius);
            DA.GetData(3, ref config.MinArea);
            DA.GetData(4, ref config.MinSetback);
            DA.GetData(5, ref config.MaxSetback);
            DA.GetData(6, ref config.MinDepth);
            DA.GetData(7, ref config.MaxDepth);

            DA.SetData(0, config.Serialize());
        }
    }

    public class CourtyardConfig : BuildingConfigComponent
    {
        public CourtyardConfig() : base("Courtyard Config", "CrtCfg", "Configure Courtyard building parameters", BuildingType.Courtyard) { }
        public override Guid ComponentGuid => new Guid("11111111-1111-1111-1111-111111111111");
        protected override Bitmap Icon => ComponentIcons.Get("MyceliumCourtyard");
    }

    public class LinearConfig : BuildingConfigComponent
    {
        public LinearConfig() : base("Linear Config", "LinCfg", "Configure Linear building parameters", BuildingType.Linear) { }
        public override Guid ComponentGuid => new Guid("22222222-2222-2222-2222-222222222222");
        protected override Bitmap Icon => ComponentIcons.Get("MyceliumLinear");
    }

    public class PointConfig : BuildingConfigComponent
    {
        public PointConfig() : base("Point Config", "PntCfg", "Configure Point building parameters", BuildingType.Point) { }
        public override Guid ComponentGuid => new Guid("33333333-3333-3333-3333-333333333333");
        protected override Bitmap Icon => ComponentIcons.Get("MyceliumPoint");
    }

    public class LShapeConfig : BuildingConfigComponent
    {
        public LShapeConfig() : base("L-Shape Config", "LCfg", "Configure L-Shape building parameters", BuildingType.LShape) { }
        public override Guid ComponentGuid => new Guid("44444444-4444-4444-4444-444444444444");
        protected override Bitmap Icon => ComponentIcons.Get("MyceliumL");
    }

    public class UShapeConfig : BuildingConfigComponent
    {
        public UShapeConfig() : base("U-Shape Config", "UCfg", "Configure U-Shape building parameters", BuildingType.UShape) { }
        public override Guid ComponentGuid => new Guid("55555555-5555-5555-5555-555555555555");
        protected override Bitmap Icon => ComponentIcons.Get("MyceliumU");
    }

    public class TallBuildingConfig : BuildingConfigComponent
    {
        public TallBuildingConfig() : base("Tall Building Config", "TallCfg", "Configure Tall Building parameters", BuildingType.Tower) { }
        public override Guid ComponentGuid => new Guid("66666666-6666-6666-6666-666666666666");
        protected override Bitmap Icon => ComponentIcons.Get("MyceliumTower");
    }
}
