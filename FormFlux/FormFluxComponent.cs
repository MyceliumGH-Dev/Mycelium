using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using FormFlux.Core;

namespace FormFlux
{
    public class FormFluxComponent : GH_Component
    {
        public FormFluxComponent()
          : base("Form Flux", "FormFlux",
              "Generate building masses with multiple typologies from parcel boundaries",
              "FormFlux", "Massing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundary", "B", "Parcel boundary curve (closed, planar)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Setback", "S", "Distance from parcel edge to building face", GH_ParamAccess.item, 3.0);
            pManager.AddNumberParameter("BuildingDepth", "D", "Depth of the building wing", GH_ParamAccess.item, 12.0);
            pManager.AddNumberParameter("MinFootprintArea", "MinA", "Minimum buildable footprint area (m²)", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("Floors_min", "Fmin", "Minimum floors", GH_ParamAccess.item, 3.0);
            pManager.AddNumberParameter("Floors_max", "Fmax", "Maximum floors", GH_ParamAccess.item, 6.0);
            pManager.AddNumberParameter("FloorHeight", "FH", "Floor-to-floor height (m)", GH_ParamAccess.item, 4);
            pManager.AddIntegerParameter("Divisions", "Div", "Subdivision recursion depth", GH_ParamAccess.item, 2);
            pManager.AddNumberParameter("StreetWidth", "SW", "Width of streets (m)", GH_ParamAccess.item, 2.0);
            pManager.AddIntegerParameter("BuildingTypes", "Types", "Allowed building types: 0=courtyard, 1=linear, 2=point, 3=l-shape, 4=u-shape", GH_ParamAccess.list);
            pManager.AddIntegerParameter("NumParks", "Parks", "Number of park parcels", GH_ParamAccess.item, 2);
            pManager.AddBooleanParameter("GenerateFloorSlabs", "Slabs", "Generate individual floor slabs",  GH_ParamAccess.item, false);
            pManager.AddTextParameter("Trees", "Trees", "Tree configuration from Tree Config component (optional)", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Seed", "Seed", "Random seed", GH_ParamAccess.item, 0);

            // Set defaults for optional lists
            pManager[9].Optional = true; // BuildingTypes
            pManager[12].Optional = true; // Trees
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
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get inputs
            Curve boundary = null;
            double setback = 3.0;
            double buildingDepth = 12.0;
            double minFootprintArea = 100.0;
            double floorsMin = 3.0;
            double floorsMax = 6.0;
            double floorHeight = 3.2;
            int divisions = 0;
            double streetWidth = 5.0;
            List<int> buildingTypeIndices = new List<int>();
            int numParks = 0;
            bool generateFloorSlabs = false;
            string treeConfig = null;
            int seed = 0;

            // Default tree parameters
            double treeDensity = 100.0;
            double minTreeDiameter = 2.0;
            double maxTreeDiameter = 5.0;
            bool generateInCourtyards = true;

            if (!DA.GetData(0, ref boundary)) return;
            DA.GetData(1, ref setback);
            DA.GetData(2, ref buildingDepth);
            DA.GetData(3, ref minFootprintArea);
            DA.GetData(4, ref floorsMin);
            DA.GetData(5, ref floorsMax);
            DA.GetData(6, ref floorHeight);
            DA.GetData(7, ref divisions);
            DA.GetData(8, ref streetWidth);
            DA.GetDataList(9, buildingTypeIndices);
            DA.GetData(10, ref numParks);
            DA.GetData(11, ref generateFloorSlabs);
            bool hasTreeConfig = DA.GetData(12, ref treeConfig);
            DA.GetData(13, ref seed);

            // Parse tree configuration if provided
            if (hasTreeConfig && !string.IsNullOrEmpty(treeConfig))
            {
                try
                {
                    var parts = treeConfig.Split('|');
                    if (parts.Length == 4)
                    {
                        treeDensity = double.Parse(parts[0]);
                        minTreeDiameter = double.Parse(parts[1]);
                        maxTreeDiameter = double.Parse(parts[2]);
                        generateInCourtyards = bool.Parse(parts[3]);
                    }
                }
                catch
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid tree configuration format");
                }
            }

            // Map building type indices to names
            // 0=courtyard, 1=linear, 2=point, 3=l-shape, 4=u-shape
            string[] typeNames = { "courtyard", "linear", "point", "l-shape", "u-shape" };
            
            var allowedTypes = new List<string>();
            if (buildingTypeIndices == null || buildingTypeIndices.Count == 0)
            {
                allowedTypes.Add("courtyard"); // Default to courtyard
            }
            else
            {
                foreach (var idx in buildingTypeIndices)
                {
                    if (idx >= 0 && idx < typeNames.Length)
                        allowedTypes.Add(typeNames[idx]);
                }
                // If no valid indices, default to courtyard
                if (allowedTypes.Count == 0)
                    allowedTypes.Add("courtyard");
            }

            // Random number generator
            var rng = new Random(seed);

            // Output lists
            var footprints = new List<Curve>();
            var masses = new List<Brep>();
            var heights = new List<double>();
            var streets = new List<Curve>();
            var floorSlabs = new List<Brep>();
            var parks = new List<Curve>();
            var courtyards = new List<Curve>();
            var trees = new List<Brep>();
            var parcels = new List<Curve>();

            // 1. Subdivide parcel
            var allParcels = ParcelSubdivision.Subdivide(boundary, divisions, minFootprintArea, streetWidth, rng);

            // Calculate streets
            var streetsDiff = Curve.CreateBooleanDifference(boundary, allParcels.ToArray(), 0.001);
            if (streetsDiff != null)
                streets.AddRange(streetsDiff);

            // Select park indices
            int numParcels = allParcels.Count;
            var parkIndices = new HashSet<int>();
            if (numParks > 0 && numParcels > 0)
            {
                int nParks = Math.Min(numParks, numParcels);
                var indices = new List<int>();
                for (int i = 0; i < numParcels; i++)
                    indices.Add(i);
                
                // Shuffle and take first n
                for (int i = 0; i < nParks; i++)
                {
                    int j = rng.Next(i, numParcels);
                    int temp = indices[i];
                    indices[i] = indices[j];
                    indices[j] = temp;
                    parkIndices.Add(indices[i]);
                }
            }

            // Process each parcel
            double totalGFA = 0.0;

            for (int i = 0; i < allParcels.Count; i++)
            {
                var pCurve = allParcels[i];

                // Check if park
                if (parkIndices.Contains(i))
                {
                    parks.Add(pCurve);
                    // Generate trees in parks
                    var parkTrees = TreeGenerator.GenerateTrees(pCurve, rng, treeDensity, minTreeDiameter, maxTreeDiameter);
                    trees.AddRange(parkTrees);
                    continue;
                }

                // Add to building parcels
                parcels.Add(pCurve);

                // Check minimum footprint area
                var plane = Plane.WorldXY;
                if (pCurve.ClosedCurveOrientation(plane) == CurveOrientation.Clockwise)
                    pCurve.Reverse();

                var buildableOffsets = GeometryHelpers.OffsetCurve(pCurve, -setback, plane);
                if (buildableOffsets == null || buildableOffsets.Length == 0)
                    continue;

                double buildableArea = 0.0;
                foreach (var bo in buildableOffsets)
                {
                    double area = GeometryHelpers.GetCurveArea(bo);
                    buildableArea += area;
                }

                if (buildableArea < minFootprintArea)
                    continue;

                // Pick random building type
                string buildingType = allowedTypes[rng.Next(allowedTypes.Count)];

                // Generate footprint
                List<Curve> blockFootprints = null;
                List<Curve> courtyardInteriors = new List<Curve>();

                switch (buildingType)
                {
                    case "linear":
                        blockFootprints = BuildingGenerators.GenerateLinearBlock(pCurve, setback, buildingDepth);
                        break;
                    case "point":
                        blockFootprints = BuildingGenerators.GeneratePointBlock(pCurve, setback, buildingDepth);
                        break;
                    case "l-shape":
                        blockFootprints = BuildingGenerators.GenerateLShape(pCurve, setback, buildingDepth, rng);
                        break;
                    case "u-shape":
                        blockFootprints = BuildingGenerators.GenerateUShape(pCurve, setback, buildingDepth, rng);
                        break;
                    default:
                        // Courtyard/perimeter block - returns tuple with courtyards
                        var result = BuildingGenerators.GeneratePerimeterBlock(pCurve, setback, buildingDepth);
                        blockFootprints = result.Item1;
                        courtyardInteriors = result.Item2;
                        break;
                }

                if (blockFootprints == null || blockFootprints.Count == 0)
                    continue;

                footprints.AddRange(blockFootprints);
                
                // Collect courtyard boundaries for output
                if (courtyardInteriors != null && courtyardInteriors.Count > 0)
                {
                    courtyards.AddRange(courtyardInteriors);
                }
                
                // Generate trees in courtyards (if enabled in tree config)
                if (generateInCourtyards && courtyardInteriors != null && courtyardInteriors.Count > 0)
                {
                    foreach (var courtyard in courtyardInteriors)
                    {
                        var courtyardTrees = TreeGenerator.GenerateTrees(courtyard, rng, treeDensity, minTreeDiameter, maxTreeDiameter);
                        trees.AddRange(courtyardTrees);
                    }
                }

                // Random height
                floorsMin = Math.Max(1.0, floorsMin);
                floorsMax = Math.Max(floorsMin, floorsMax);
                double avgFloors = rng.NextDouble() * (floorsMax - floorsMin) + floorsMin;
                double height = avgFloors * floorHeight;

                for (int j = 0; j < blockFootprints.Count; j++)
                    heights.Add(height);

                // Extrude to create masses
                var parcelMasses = GeometryHelpers.ExtrudeFootprints(blockFootprints, height);
                masses.AddRange(parcelMasses);

                // Calculate GFA
                double footprintArea = 0.0;
                foreach (var fp in blockFootprints)
                {
                    footprintArea += GeometryHelpers.GetCurveArea(fp);
                }
                totalGFA += footprintArea * avgFloors;

                // Generate floor slabs (optional)
                if (generateFloorSlabs)
                {
                    int numFloors = (int)Math.Ceiling(avgFloors);
                    for (int f = 0; f < numFloors; f++)
                    {
                        double z = f * floorHeight;
                        foreach (var fp in blockFootprints)
                        {
                            var planars = Brep.CreatePlanarBreps(fp, 0.001);
                            if (planars != null)
                            {
                                foreach (var planar in planars)
                                {
                                    var moved = planar.DuplicateBrep();
                                    moved.Translate(new Vector3d(0, 0, z));
                                    floorSlabs.Add(moved);
                                }
                            }
                        }
                    }
                }
            }

            // Calculate metrics
            double originalArea = GeometryHelpers.GetCurveArea(boundary);
            double far = originalArea > 0 ? totalGFA / originalArea : 0.0;
            double totalGIA = totalGFA * 0.85; // 85% efficiency
            double totalNIA = totalGIA * 0.77; // 77% of GIA
            int totalUnits = (int)(totalNIA / 75.0); // 75m² per unit

            string metrics = $"--- Area Metrics ---\n";
            metrics += $"Parcel Area: {originalArea:F0} m²\n";
            metrics += $"Total GFA: {totalGFA:F0} m²\n";
            metrics += $"Total GIA: {totalGIA:F0} m²\n";
            metrics += $"Total NIA: {totalNIA:F0} m²\n";
            metrics += $"FAR: {far:F2}\n\n";
            metrics += $"--- Quantities ---\n";
            metrics += $"Parcels: {allParcels.Count}\n";
            metrics += $"Buildings: {masses.Count}\n";
            metrics += $"Parks: {parks.Count}\n";
            metrics += $"Trees: {trees.Count}\n";
            metrics += $"Total Units: {totalUnits}\n";

            // Set outputs
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
        }

        protected override Bitmap Icon => Properties.Resources.icon_24x24;

        public override Guid ComponentGuid => new Guid("8DD5A26C-63F9-4E4F-9A7B-6C5B8D1E4F3A");
    }
}
