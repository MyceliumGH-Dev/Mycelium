using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Grasshopper.Kernel;
using GH_IO.Serialization;
using Rhino.Geometry;
using Mycelium.Core;

namespace Mycelium.Components
{
    /// <summary>
    /// Main generator: subdivides a parcel into blocks and streets, assigns building
    /// typologies, and produces massing geometry with parks, trees, and metrics.
    /// </summary>
    public class MassingGeneratorComponent : GH_Component
    {
        private const string NetworkTypeKey = "StreetNetworkType";
        private const string OrthogonalGridTypeKey = "OrthogonalGridType";
        private const string IrregularGridTypeKey = "IrregularGridType";
        private const string DiagonalGridTypeKey = "DiagonalGridType";
        private const string RadialGridTypeKey = "RadialGridType";
        private StreetNetworkType _networkType = StreetNetworkType.IrregularGrid;
        private OrthogonalGridType _orthogonalGridType = OrthogonalGridType.Regular;
        private IrregularGridType _irregularGridType = IrregularGridType.RecursiveOrthogonal;
        private DiagonalGridType _diagonalGridType = DiagonalGridType.SingleAxis;
        private RadialConcentricGridType _radialGridType = RadialConcentricGridType.CivicCore;

        public MassingGeneratorComponent()
          : base("Massing Generator", "Massing",
              "Generate building masses with multiple typologies from a parcel boundary",
              "Mycelium", "Massing")
        {
            Message = CurrentNetworkLabel();
        }

        // GUID predates the Mycelium rename; existing Grasshopper files depend on it.
        public override Guid ComponentGuid => new Guid("8DD5A26C-63F9-4E4F-9A7B-6C5B8D1E4F3A");

        protected override Bitmap Icon => ComponentIcons.Get("MyceliumMassing");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundary", "B", "Parcel boundary curve (closed, planar)", GH_ParamAccess.item);
            pManager.AddNumberParameter("FloorHeight", "FH", "Floor-to-floor height (m)", GH_ParamAccess.item, 4);
            pManager.AddIntegerParameter("Divisions", "Div", "Subdivision recursion depth", GH_ParamAccess.item, 2);
            pManager.AddNumberParameter("StreetWidth", "SW", "Width of streets (m)", GH_ParamAccess.item, 2.0);
            pManager.AddTextParameter("BuildingConfigs", "Configs", "List of building configurations from Config components", GH_ParamAccess.list);
            pManager.AddIntegerParameter("NumParks", "Parks", "Number of park parcels", GH_ParamAccess.item, 2);
            pManager.AddBooleanParameter("GenerateFloorSlabs", "Slabs", "Generate individual floor slabs", GH_ParamAccess.item, false);
            pManager.AddTextParameter("Trees", "Trees", "Tree configuration from Tree Config component (optional)", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Seed", "Seed", "Random seed", GH_ParamAccess.item, 0);
            pManager.AddVectorParameter("AnalysisDirection", "Dir",
                "Horizontal analysis direction for directional frontal area density (lambda_f)",
                GH_ParamAccess.item, Vector3d.XAxis);

            pManager[4].Optional = true; // BuildingConfigs
            pManager[7].Optional = true; // Trees
            pManager[9].Optional = true; // AnalysisDirection
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Footprints", "F", "Building footprint curves", GH_ParamAccess.list);
            pManager.AddBrepParameter("Masses", "M", "Building mass geometry", GH_ParamAccess.list);
            pManager.AddNumberParameter("Heights", "H", "Building heights", GH_ParamAccess.list);
            pManager.AddCurveParameter("Streets", "Str", "Street geometry", GH_ParamAccess.list);
            pManager.AddBrepParameter("FloorSlabs", "FS", "Individual floor slabs", GH_ParamAccess.list);
            pManager.AddCurveParameter("Parks", "P", "Park boundaries", GH_ParamAccess.list);
            pManager.AddCurveParameter("Courtyards", "Court", "Courtyard boundaries (for tree generation)", GH_ParamAccess.list);
            pManager.AddBrepParameter("Trees", "T", "Tree spheres", GH_ParamAccess.list);
            pManager.AddCurveParameter("Parcels", "Parc", "Building parcel boundaries", GH_ParamAccess.list);
            pManager.AddTextParameter("Metrics", "Met", "Area and unit metrics", GH_ParamAccess.item);
            pManager.AddTextParameter("MorphologyMetrics", "Morph",
                "Urban morphology indicators including lambda_p, directional lambda_f, open/park ratios, and height statistics",
                GH_ParamAccess.item);
            pManager.AddTextParameter("CaseManifest", "JSON",
                "Versioned JSON case manifest containing inputs, provenance, counts, development metrics, and morphology metrics",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve boundary = null;
            double floorHeight = 3.2;
            int divisions = 0;
            double streetWidth = 5.0;
            var buildingConfigsRaw = new List<string>();
            int numParks = 0;
            bool generateFloorSlabs = false;
            string treeConfigRaw = null;
            int seed = 0;
            Vector3d analysisDirection = Vector3d.XAxis;

            if (!DA.GetData(0, ref boundary)) return;
            DA.GetData(1, ref floorHeight);
            DA.GetData(2, ref divisions);
            DA.GetData(3, ref streetWidth);
            DA.GetDataList(4, buildingConfigsRaw);
            DA.GetData(5, ref numParks);
            DA.GetData(6, ref generateFloorSlabs);
            bool hasTreeConfig = DA.GetData(7, ref treeConfigRaw);
            DA.GetData(8, ref seed);
            DA.GetData(9, ref analysisDirection);

            // Tree configuration (optional input, defaults otherwise)
            var treeConfig = TreeConfig.Default;
            if (hasTreeConfig && !string.IsNullOrEmpty(treeConfigRaw) && !TreeConfig.TryParse(treeConfigRaw, out treeConfig))
            {
                treeConfig = TreeConfig.Default;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid tree configuration format");
            }

            // Building configurations; the smallest MinArea drives the subdivision
            var allowedConfigs = new List<BuildingTypeConfig>();
            double globalMinArea = double.MaxValue;

            foreach (var cfgStr in buildingConfigsRaw)
            {
                if (BuildingTypeConfig.TryParse(cfgStr, out var config))
                {
                    allowedConfigs.Add(config);
                    if (config.MinArea < globalMinArea)
                        globalMinArea = config.MinArea;
                }
            }

            if (allowedConfigs.Count == 0)
            {
                allowedConfigs.Add(BuildingTypeConfig.Default);
                globalMinArea = BuildingTypeConfig.Default.MinArea;
            }

            var rng = new Random(seed);

            var footprints = new List<Curve>();
            var masses = new List<Brep>();
            var heights = new List<double>();
            var streets = new List<Curve>();
            var floorSlabs = new List<Brep>();
            var parks = new List<Curve>();
            var courtyards = new List<Curve>();
            var trees = new List<Brep>();
            var parcels = new List<Curve>();

            // 1. Subdivide the boundary into parcels separated by streets
            var allParcels = ParcelSubdivision.Subdivide(boundary, divisions, globalMinArea,
                streetWidth, rng, _networkType, _orthogonalGridType, _irregularGridType,
                _diagonalGridType, _radialGridType);

            // 2. Streets are the leftover space between boundary and parcels
            var streetsDiff = Curve.CreateBooleanDifference(boundary, allParcels.ToArray(), 0.001);
            if (streetsDiff != null)
                streets.AddRange(streetsDiff);

            // 3. Randomly select park parcels (partial Fisher-Yates shuffle)
            var parkIndices = SelectParkIndices(allParcels.Count, numParks, rng);

            // 4. Populate each parcel
            double totalGFA = 0.0;

            for (int i = 0; i < allParcels.Count; i++)
            {
                var parcelCurve = allParcels[i];

                if (parkIndices.Contains(i))
                {
                    parks.Add(parcelCurve);
                    trees.AddRange(TreeGenerator.GenerateTrees(parcelCurve, rng,
                        treeConfig.DensityPercent, treeConfig.MinDiameter, treeConfig.MaxDiameter));
                    continue;
                }

                parcels.Add(parcelCurve);
                totalGFA += GenerateParcelBuildings(parcelCurve, allowedConfigs, rng, floorHeight,
                    generateFloorSlabs, hasTreeConfig, treeConfig,
                    footprints, masses, heights, floorSlabs, courtyards, trees);
            }

            // 5. Development and morphology metrics
            var development = CalculateDevelopmentMetrics(boundary, totalGFA);
            string metrics = BuildMetrics(development, allParcels.Count, masses.Count, parks.Count, trees.Count);
            var morphology = MorphologyMetrics.Calculate(boundary, footprints, masses, parks, analysisDirection);

            // 6. Reproducible parameter/provenance sidecar. Geometry can be exported independently.
            var manifest = new CaseManifest
            {
                Generator = new GeneratorProvenance { Version = CaseManifest.InstalledVersion() },
                Parameters = new GenerationParameters
                {
                    Seed = seed,
                    ModelUnits = CaseManifest.ActiveModelUnits(),
                    FloorHeight = floorHeight,
                    Divisions = divisions,
                    StreetWidth = streetWidth,
                    RequestedParks = numParks,
                    GenerateFloorSlabs = generateFloorSlabs,
                    StreetNetworkFamily = NetworkTypeLabel(_networkType),
                    StreetNetworkSubtype = CurrentNetworkLabel(),
                    BuildingConfigurations = allowedConfigs.Select(config => config.Serialize()).ToArray(),
                    TreeConfiguration = treeConfig.Serialize(),
                    TreeConfigurationProvided = hasTreeConfig,
                    AnalysisDirection = new Direction2D
                    {
                        X = morphology.AnalysisDirectionX,
                        Y = morphology.AnalysisDirectionY
                    }
                },
                Geometry = new GeometrySummary
                {
                    Footprints = footprints.Count,
                    Masses = masses.Count,
                    Streets = streets.Count,
                    FloorSlabs = floorSlabs.Count,
                    Parks = parks.Count,
                    Courtyards = courtyards.Count,
                    Trees = trees.Count,
                    Parcels = parcels.Count
                },
                Development = development,
                Morphology = morphology
            };
            manifest.CaseId = manifest.CalculateCaseId();

            DA.SetDataList(0, footprints);
            DA.SetDataList(1, masses);
            DA.SetDataList(2, heights);
            DA.SetDataList(3, streets);
            DA.SetDataList(4, floorSlabs);
            DA.SetDataList(5, parks);
            DA.SetDataList(6, courtyards);
            DA.SetDataList(7, trees);
            DA.SetDataList(8, parcels);
            DA.SetData(9, metrics);
            DA.SetData(10, morphology.ToDisplayString());
            DA.SetData(11, manifest.ToJson());
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);

            var networkMenu = Menu_AppendItem(menu, "Street Network");
            var irregularMenu = Menu_AppendItem(networkMenu.DropDown, "Irregular Grid");
            irregularMenu.Checked = _networkType == StreetNetworkType.IrregularGrid;
            foreach (IrregularGridType type in Enum.GetValues(typeof(IrregularGridType)))
            {
                var selectedType = type;
                Menu_AppendItem(irregularMenu.DropDown, IrregularGridTypeLabel(type),
                    (sender, args) => SetIrregularGridType(selectedType), true,
                    _networkType == StreetNetworkType.IrregularGrid && _irregularGridType == type);
            }

            var orthogonalMenu = Menu_AppendItem(networkMenu.DropDown, "Orthogonal Grid");
            orthogonalMenu.Checked = _networkType == StreetNetworkType.OrthogonalGrid;
            foreach (OrthogonalGridType type in Enum.GetValues(typeof(OrthogonalGridType)))
            {
                var selectedType = type;
                Menu_AppendItem(orthogonalMenu.DropDown, OrthogonalGridTypeLabel(type),
                    (sender, args) => SetOrthogonalGridType(selectedType), true,
                    _networkType == StreetNetworkType.OrthogonalGrid && _orthogonalGridType == type);
            }

            var diagonalMenu = Menu_AppendItem(networkMenu.DropDown, "Diagonal Grid");
            diagonalMenu.Checked = _networkType == StreetNetworkType.DiagonalGrid;
            foreach (DiagonalGridType type in Enum.GetValues(typeof(DiagonalGridType)))
            {
                var selectedType = type;
                Menu_AppendItem(diagonalMenu.DropDown, DiagonalGridTypeLabel(type),
                    (sender, args) => SetDiagonalGridType(selectedType), true,
                    _networkType == StreetNetworkType.DiagonalGrid && _diagonalGridType == type);
            }
            var radialMenu = Menu_AppendItem(networkMenu.DropDown, "Radial-Concentric Grid");
            radialMenu.Checked = _networkType == StreetNetworkType.RadialConcentricGrid;
            foreach (RadialConcentricGridType type in Enum.GetValues(typeof(RadialConcentricGridType)))
            {
                var selectedType = type;
                Menu_AppendItem(radialMenu.DropDown, RadialGridTypeLabel(type),
                    (sender, args) => SetRadialGridType(selectedType), true,
                    _networkType == StreetNetworkType.RadialConcentricGrid && _radialGridType == type);
            }
        }

        private void SetNetworkType(StreetNetworkType networkType)
        {
            if (_networkType == networkType)
                return;

            RecordUndoEvent("Street Network");
            _networkType = networkType;
            Message = CurrentNetworkLabel();
            ExpireSolution(true);
        }

        private void SetOrthogonalGridType(OrthogonalGridType gridType)
        {
            if (_networkType == StreetNetworkType.OrthogonalGrid && _orthogonalGridType == gridType)
                return;

            RecordUndoEvent("Orthogonal Grid Type");
            _networkType = StreetNetworkType.OrthogonalGrid;
            _orthogonalGridType = gridType;
            Message = CurrentNetworkLabel();
            ExpireSolution(true);
        }

        private void SetIrregularGridType(IrregularGridType gridType)
        {
            if (_networkType == StreetNetworkType.IrregularGrid && _irregularGridType == gridType)
                return;

            RecordUndoEvent("Irregular Grid Type");
            _networkType = StreetNetworkType.IrregularGrid;
            _irregularGridType = gridType;
            Message = CurrentNetworkLabel();
            ExpireSolution(true);
        }

        private void SetDiagonalGridType(DiagonalGridType gridType)
        {
            if (_networkType == StreetNetworkType.DiagonalGrid && _diagonalGridType == gridType)
                return;

            RecordUndoEvent("Diagonal Grid Type");
            _networkType = StreetNetworkType.DiagonalGrid;
            _diagonalGridType = gridType;
            Message = CurrentNetworkLabel();
            ExpireSolution(true);
        }

        private void SetRadialGridType(RadialConcentricGridType gridType)
        {
            if (_networkType == StreetNetworkType.RadialConcentricGrid && _radialGridType == gridType)
                return;

            RecordUndoEvent("Radial-Concentric Grid Type");
            _networkType = StreetNetworkType.RadialConcentricGrid;
            _radialGridType = gridType;
            Message = CurrentNetworkLabel();
            ExpireSolution(true);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetInt32(NetworkTypeKey, (int)_networkType);
            writer.SetInt32(OrthogonalGridTypeKey, (int)_orthogonalGridType);
            writer.SetInt32(IrregularGridTypeKey, (int)_irregularGridType);
            writer.SetInt32(DiagonalGridTypeKey, (int)_diagonalGridType);
            writer.SetInt32(RadialGridTypeKey, (int)_radialGridType);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            int storedType = (int)StreetNetworkType.IrregularGrid;
            if (reader.ItemExists(NetworkTypeKey))
                storedType = reader.GetInt32(NetworkTypeKey);

            _networkType = Enum.IsDefined(typeof(StreetNetworkType), storedType)
                ? (StreetNetworkType)storedType
                : StreetNetworkType.IrregularGrid;

            int storedOrthogonalType = (int)OrthogonalGridType.Regular;
            if (reader.ItemExists(OrthogonalGridTypeKey))
                storedOrthogonalType = reader.GetInt32(OrthogonalGridTypeKey);
            _orthogonalGridType = Enum.IsDefined(typeof(OrthogonalGridType), storedOrthogonalType)
                ? (OrthogonalGridType)storedOrthogonalType
                : OrthogonalGridType.Regular;

            int storedIrregularType = (int)IrregularGridType.RecursiveOrthogonal;
            if (reader.ItemExists(IrregularGridTypeKey))
                storedIrregularType = reader.GetInt32(IrregularGridTypeKey);
            _irregularGridType = Enum.IsDefined(typeof(IrregularGridType), storedIrregularType)
                ? (IrregularGridType)storedIrregularType
                : IrregularGridType.RecursiveOrthogonal;

            int storedDiagonalType = (int)DiagonalGridType.SingleAxis;
            if (reader.ItemExists(DiagonalGridTypeKey))
                storedDiagonalType = reader.GetInt32(DiagonalGridTypeKey);
            _diagonalGridType = Enum.IsDefined(typeof(DiagonalGridType), storedDiagonalType)
                ? (DiagonalGridType)storedDiagonalType
                : DiagonalGridType.SingleAxis;

            int storedRadialType = (int)RadialConcentricGridType.CivicCore;
            if (reader.ItemExists(RadialGridTypeKey))
                storedRadialType = reader.GetInt32(RadialGridTypeKey);
            _radialGridType = Enum.IsDefined(typeof(RadialConcentricGridType), storedRadialType)
                ? (RadialConcentricGridType)storedRadialType
                : RadialConcentricGridType.CivicCore;

            Message = CurrentNetworkLabel();
            return base.Read(reader);
        }

        private string CurrentNetworkLabel()
        {
            if (_networkType == StreetNetworkType.OrthogonalGrid)
                return OrthogonalGridTypeLabel(_orthogonalGridType);
            if (_networkType == StreetNetworkType.IrregularGrid)
                return IrregularGridTypeLabel(_irregularGridType);
            if (_networkType == StreetNetworkType.DiagonalGrid)
                return DiagonalGridTypeLabel(_diagonalGridType);
            if (_networkType == StreetNetworkType.RadialConcentricGrid)
                return RadialGridTypeLabel(_radialGridType);
            return NetworkTypeLabel(_networkType);
        }

        private static string RadialGridTypeLabel(RadialConcentricGridType gridType)
        {
            switch (gridType)
            {
                case RadialConcentricGridType.PolygonalRadial:
                    return "Polygonal Radial";
                case RadialConcentricGridType.FanPlan:
                    return "Fan Plan";
                case RadialConcentricGridType.CivicCore:
                default:
                    return "Civic Core";
            }
        }

        private static string DiagonalGridTypeLabel(DiagonalGridType gridType)
        {
            switch (gridType)
            {
                case DiagonalGridType.CrossAxes:
                    return "Cross Axes";
                case DiagonalGridType.OrthogonalOverlay:
                    return "Orthogonal Overlay";
                case DiagonalGridType.SingleAxis:
                default:
                    return "Single Axis";
            }
        }

        private static string IrregularGridTypeLabel(IrregularGridType gridType)
        {
            switch (gridType)
            {
                case IrregularGridType.DeformedGrid:
                    return "Deformed Grid";
                case IrregularGridType.StaggeredGrid:
                    return "Staggered Grid";
                case IrregularGridType.RecursiveOrthogonal:
                default:
                    return "Recursive Orthogonal";
            }
        }

        private static string OrthogonalGridTypeLabel(OrthogonalGridType gridType)
        {
            switch (gridType)
            {
                case OrthogonalGridType.Rectangular:
                    return "Rectangular Grid";
                case OrthogonalGridType.Cerda:
                    return "Cerdà Grid";
                case OrthogonalGridType.HierarchicalSuperblock:
                    return "Hierarchical Superblock";
                case OrthogonalGridType.Regular:
                default:
                    return "Regular Grid";
            }
        }

        private static string NetworkTypeLabel(StreetNetworkType networkType)
        {
            switch (networkType)
            {
                case StreetNetworkType.OrthogonalGrid:
                    return "Orthogonal Grid";
                case StreetNetworkType.DiagonalGrid:
                    return "Diagonal Grid";
                case StreetNetworkType.RadialConcentricGrid:
                    return "Radial–Concentric Grid";
                case StreetNetworkType.IrregularGrid:
                default:
                    return "Irregular Grid";
            }
        }

        /// <summary>
        /// Picks up to <paramref name="numParks"/> distinct parcel indices at random.
        /// </summary>
        private static HashSet<int> SelectParkIndices(int numParcels, int numParks, Random rng)
        {
            var parkIndices = new HashSet<int>();
            if (numParks <= 0 || numParcels <= 0)
                return parkIndices;

            int nParks = Math.Min(numParks, numParcels);
            var indices = new List<int>();
            for (int i = 0; i < numParcels; i++)
                indices.Add(i);

            for (int i = 0; i < nParks; i++)
            {
                int j = rng.Next(i, numParcels);
                (indices[i], indices[j]) = (indices[j], indices[i]);
                parkIndices.Add(indices[i]);
            }

            return parkIndices;
        }

        /// <summary>
        /// Generates the building on one parcel: picks a random allowed typology, creates its
        /// footprint, extrudes the mass, and optionally adds floor slabs and courtyard trees.
        /// Returns the gross floor area contributed by this parcel.
        /// </summary>
        private double GenerateParcelBuildings(Curve parcelCurve, List<BuildingTypeConfig> allowedConfigs,
            Random rng, double floorHeight, bool generateFloorSlabs, bool hasTreeConfig, TreeConfig treeConfig,
            List<Curve> footprints, List<Brep> masses, List<double> heights,
            List<Brep> floorSlabs, List<Curve> courtyards, List<Brep> trees)
        {
            var plane = Plane.WorldXY;
            if (parcelCurve.ClosedCurveOrientation(plane) == CurveOrientation.Clockwise)
                parcelCurve.Reverse();

            // Pick a random building config first so setback/depth ranges come from it
            var selectedConfig = allowedConfigs[rng.Next(allowedConfigs.Count)];

            double sMin = Math.Max(0.0, selectedConfig.MinSetback);
            double sMax = Math.Max(sMin, selectedConfig.MaxSetback);
            double setback = rng.NextDouble() * (sMax - sMin) + sMin;

            double dMin = Math.Max(1.0, selectedConfig.MinDepth);
            double dMax = Math.Max(dMin, selectedConfig.MaxDepth);
            double depth = rng.NextDouble() * (dMax - dMin) + dMin;

            var buildableOffsets = GeometryHelpers.OffsetCurve(parcelCurve, -setback, plane);
            if (buildableOffsets == null || buildableOffsets.Length == 0)
                return 0.0;

            double buildableArea = 0.0;
            foreach (var bo in buildableOffsets)
                buildableArea += GeometryHelpers.GetCurveArea(bo);

            // Skip parcels too small for the selected building type
            if (buildableArea < selectedConfig.MinArea)
                return 0.0;

            var (blockFootprints, courtyardInteriors) = GenerateFootprints(selectedConfig.Type, parcelCurve, setback, depth, rng);

            if (blockFootprints == null || blockFootprints.Count == 0)
                return 0.0;

            if (selectedConfig.CornerRadius > 0.01)
                blockFootprints = ApplyCornerRadius(blockFootprints, selectedConfig.CornerRadius);

            footprints.AddRange(blockFootprints);

            if (courtyardInteriors.Count > 0)
            {
                courtyards.AddRange(courtyardInteriors);

                if (hasTreeConfig && treeConfig.GenerateInCourtyards)
                {
                    foreach (var courtyard in courtyardInteriors)
                        trees.AddRange(TreeGenerator.GenerateTrees(courtyard, rng,
                            treeConfig.DensityPercent, treeConfig.MinDiameter, treeConfig.MaxDiameter));
                }
            }

            // Random height within the config's floor range
            double fMin = Math.Max(1.0, selectedConfig.MinFloors);
            double fMax = Math.Max(fMin, selectedConfig.MaxFloors);
            double avgFloors = rng.NextDouble() * (fMax - fMin) + fMin;
            double height = avgFloors * floorHeight;

            for (int j = 0; j < blockFootprints.Count; j++)
                heights.Add(height);

            masses.AddRange(GeometryHelpers.ExtrudeFootprints(blockFootprints, height));

            double footprintArea = 0.0;
            foreach (var fp in blockFootprints)
                footprintArea += GeometryHelpers.GetCurveArea(fp);

            if (generateFloorSlabs)
                AddFloorSlabs(blockFootprints, avgFloors, floorHeight, floorSlabs);

            return footprintArea * avgFloors;
        }

        /// <summary>
        /// Dispatches to the footprint generator for the given typology.
        /// The courtyard type falls back to a linear block when generation fails
        /// (for example when the parcel is too small).
        /// </summary>
        private static (List<Curve> footprints, List<Curve> courtyards) GenerateFootprints(
            BuildingType type, Curve parcelCurve, double setback, double depth, Random rng)
        {
            switch (type)
            {
                case BuildingType.Linear:
                    return (BuildingGenerators.GenerateLinearBlock(parcelCurve, setback, depth), new List<Curve>());
                case BuildingType.Point:
                    return (BuildingGenerators.GeneratePointBlock(parcelCurve, setback, depth), new List<Curve>());
                case BuildingType.LShape:
                    return (BuildingGenerators.GenerateLShape(parcelCurve, setback, depth, rng), new List<Curve>());
                case BuildingType.UShape:
                    return (BuildingGenerators.GenerateUShape(parcelCurve, setback, depth, rng), new List<Curve>());
                case BuildingType.Tower:
                    return (BuildingGenerators.GenerateTallBuilding(parcelCurve, setback, depth), new List<Curve>());
                case BuildingType.Courtyard:
                default:
                    var (footprints, courtyards) = BuildingGenerators.GeneratePerimeterBlock(parcelCurve, setback, depth);
                    if (footprints.Count == 0)
                        return (BuildingGenerators.GenerateLinearBlock(parcelCurve, setback, depth), new List<Curve>());
                    return (footprints, courtyards);
            }
        }

        private static List<Curve> ApplyCornerRadius(List<Curve> blockFootprints, double cornerRadius)
        {
            var rounded = new List<Curve>();
            foreach (var fp in blockFootprints)
            {
                var filleted = Curve.CreateFilletCornersCurve(fp, cornerRadius, 0.001, 0.001);
                rounded.Add(filleted ?? fp);
            }
            return rounded;
        }

        private static void AddFloorSlabs(List<Curve> blockFootprints, double avgFloors, double floorHeight, List<Brep> floorSlabs)
        {
            int numFloors = (int)Math.Ceiling(avgFloors);
            for (int f = 0; f < numFloors; f++)
            {
                double z = f * floorHeight;
                foreach (var fp in blockFootprints)
                {
                    var planars = Brep.CreatePlanarBreps(fp, 0.001);
                    if (planars == null)
                        continue;

                    foreach (var planar in planars)
                    {
                        var moved = planar.DuplicateBrep();
                        moved.Translate(new Vector3d(0, 0, z));
                        floorSlabs.Add(moved);
                    }
                }
            }
        }

        private static DevelopmentMetrics CalculateDevelopmentMetrics(Curve boundary, double totalGFA)
        {
            double siteArea = GeometryHelpers.GetCurveArea(boundary);
            double far = siteArea > 0 ? totalGFA / siteArea : 0.0;
            double totalGIA = totalGFA * 0.85;  // 85% gross internal efficiency
            double totalNIA = totalGIA * 0.77;  // 77% net internal efficiency
            int totalUnits = (int)(totalNIA / 75.0);  // 75 m² per unit

            return new DevelopmentMetrics
            {
                SiteArea = siteArea,
                GrossFloorArea = totalGFA,
                GrossInternalArea = totalGIA,
                NetInternalArea = totalNIA,
                FloorAreaRatio = far,
                EstimatedUnits = totalUnits
            };
        }

        private static string BuildMetrics(DevelopmentMetrics development, int parcelCount,
            int buildingCount, int parkCount, int treeCount)
        {

            var metrics = new StringBuilder();
            metrics.AppendLine("--- Area Metrics ---");
            metrics.AppendLine($"Parcel Area: {development.SiteArea:F0} m²");
            metrics.AppendLine($"Total GFA: {development.GrossFloorArea:F0} m²");
            metrics.AppendLine($"Total GIA: {development.GrossInternalArea:F0} m²");
            metrics.AppendLine($"Total NIA: {development.NetInternalArea:F0} m²");
            metrics.AppendLine($"FAR: {development.FloorAreaRatio:F2}");
            metrics.AppendLine();
            metrics.AppendLine("--- Quantities ---");
            metrics.AppendLine($"Parcels: {parcelCount}");
            metrics.AppendLine($"Buildings: {buildingCount}");
            metrics.AppendLine($"Parks: {parkCount}");
            metrics.AppendLine($"Trees: {treeCount}");
            metrics.Append($"Total Units: {development.EstimatedUnits}");
            return metrics.ToString();
        }
    }
}
