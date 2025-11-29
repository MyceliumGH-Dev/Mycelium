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
              "FormFlux", "Main")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundary", "B", "Parcel boundary curve (closed, planar)", GH_ParamAccess.item);
            pManager.AddNumberParameter("FloorHeight", "FH", "Floor-to-floor height (m)", GH_ParamAccess.item, 4);
            pManager.AddIntegerParameter("Divisions", "Div", "Subdivision recursion depth", GH_ParamAccess.item, 2);
            pManager.AddNumberParameter("StreetWidth", "SW", "Width of streets (m)", GH_ParamAccess.item, 2.0);
            pManager.AddTextParameter("BuildingConfigs", "Configs", "List of building configurations from Config components", GH_ParamAccess.list);
            pManager.AddIntegerParameter("NumParks", "Parks", "Number of park parcels", GH_ParamAccess.item, 2);
            pManager.AddBooleanParameter("GenerateFloorSlabs", "Slabs", "Generate individual floor slabs",  GH_ParamAccess.item, false);
            pManager.AddTextParameter("Trees", "Trees", "Tree configuration from Tree Config component (optional)", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Seed", "Seed", "Random seed", GH_ParamAccess.item, 0);

            // Set defaults for optional lists
            pManager[4].Optional = true; // BuildingConfigs
            pManager[7].Optional = true; // Trees
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

        private struct BuildingConfig
        {
            public int TypeIndex;
            public double MinFloors;
            public double MaxFloors;
            public double CornerRadius;
            public double MinArea;
            public double MinSetback;
            public double MaxSetback;
            public double MinDepth;
            public double MaxDepth;
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get inputs
            Curve boundary = null;
            double floorHeight = 3.2;
            int divisions = 0;
            double streetWidth = 5.0;
            List<string> buildingConfigsRaw = new List<string>();
            int numParks = 0;
            bool generateFloorSlabs = false;
            string treeConfig = null;
            int seed = 0;

            // Default tree parameters
            double treeDensity = 10.0;
            double minTreeDiameter = 2.0;
            double maxTreeDiameter = 5.0;
            bool generateInCourtyards = true;

            if (!DA.GetData(0, ref boundary)) return;
            DA.GetData(1, ref floorHeight);
            DA.GetData(2, ref divisions);
            DA.GetData(3, ref streetWidth);
            DA.GetDataList(4, buildingConfigsRaw);
            DA.GetData(5, ref numParks);
            DA.GetData(6, ref generateFloorSlabs);
            bool hasTreeConfig = DA.GetData(7, ref treeConfig);
            DA.GetData(8, ref seed);

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

            // Parse building configurations
            var allowedConfigs = new List<BuildingConfig>();
            double globalMinArea = double.MaxValue; // For subdivision

            if (buildingConfigsRaw != null && buildingConfigsRaw.Count > 0)
            {
                foreach (var cfgStr in buildingConfigsRaw)
                {
                    try
                    {
                        var parts = cfgStr.Split('|');
                        if (parts.Length >= 3)
                        {
                            var config = new BuildingConfig
                            {
                                TypeIndex = int.Parse(parts[0]),
                                MinFloors = double.Parse(parts[1]),
                                MaxFloors = double.Parse(parts[2]),
                                CornerRadius = 0.0,
                                MinArea = 100.0,
                                MinSetback = 3.0,
                                MaxSetback = 3.0,
                                MinDepth = 12.0,
                                MaxDepth = 12.0
                            };
                            
                            if (parts.Length >= 4)
                                config.CornerRadius = double.Parse(parts[3]);
                            
                            if (parts.Length >= 5)
                                config.MinArea = double.Parse(parts[4]);

                            if (parts.Length >= 7)
                            {
                                config.MinSetback = double.Parse(parts[5]);
                                config.MaxSetback = double.Parse(parts[6]);
                            }

                            if (parts.Length >= 9)
                            {
                                config.MinDepth = double.Parse(parts[7]);
                                config.MaxDepth = double.Parse(parts[8]);
                            }
                            
                            allowedConfigs.Add(config);
                            
                            // Track global min area for subdivision
                            if (config.MinArea < globalMinArea)
                                globalMinArea = config.MinArea;
                        }
                    }
                    catch { /* Ignore invalid configs */ }
                }
            }

            // Default config if none provided (Courtyard, 3-6 floors)
            if (allowedConfigs.Count == 0)
            {
                allowedConfigs.Add(new BuildingConfig { 
                    TypeIndex = 0, 
                    MinFloors = 3.0, 
                    MaxFloors = 6.0, 
                    CornerRadius = 0.0, 
                    MinArea = 100.0,
                    MinSetback = 3.0,
                    MaxSetback = 3.0,
                    MinDepth = 12.0,
                    MaxDepth = 12.0
                });
                globalMinArea = 100.0;
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

            // 1. Subdivide parcel using the smallest min area from all configs
            var allParcels = ParcelSubdivision.Subdivide(boundary, divisions, globalMinArea, streetWidth, rng);

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

                // Pick random building config FIRST
                var selectedConfig = allowedConfigs[rng.Next(allowedConfigs.Count)];
                
                // Random setback
                double sMin = Math.Max(0.0, selectedConfig.MinSetback);
                double sMax = Math.Max(sMin, selectedConfig.MaxSetback);
                double currentSetback = rng.NextDouble() * (sMax - sMin) + sMin;

                // Random depth
                double dMin = Math.Max(1.0, selectedConfig.MinDepth);
                double dMax = Math.Max(dMin, selectedConfig.MaxDepth);
                double currentDepth = rng.NextDouble() * (dMax - dMin) + dMin;

                var buildableOffsets = GeometryHelpers.OffsetCurve(pCurve, -currentSetback, plane);
                if (buildableOffsets == null || buildableOffsets.Length == 0)
                    continue;

                double buildableArea = 0.0;
                foreach (var bo in buildableOffsets)
                {
                    double area = GeometryHelpers.GetCurveArea(bo);
                    buildableArea += area;
                }

                // Check if this parcel is large enough for the selected building type
                if (buildableArea < selectedConfig.MinArea)
                    continue;

                int typeIndex = selectedConfig.TypeIndex;

                // Generate footprint
                List<Curve> blockFootprints = null;
                List<Curve> courtyardInteriors = new List<Curve>();

                switch (typeIndex)
                {
                    case 1: // Linear
                        blockFootprints = BuildingGenerators.GenerateLinearBlock(pCurve, currentSetback, currentDepth);
                        break;
                    case 2: // Point
                        blockFootprints = BuildingGenerators.GeneratePointBlock(pCurve, currentSetback, currentDepth);
                        break;
                    case 3: // L-Shape
                        blockFootprints = BuildingGenerators.GenerateLShape(pCurve, currentSetback, currentDepth, rng);
                        break;
                    case 4: // U-Shape
                        blockFootprints = BuildingGenerators.GenerateUShape(pCurve, currentSetback, currentDepth, rng);
                        break;
                    case 5: // Tall Building
                        blockFootprints = BuildingGenerators.GenerateTallBuilding(pCurve, currentSetback, currentDepth);
                        break;
                    default: // 0 = Courtyard
                        // Courtyard/perimeter block - returns tuple with courtyards
                        var result = BuildingGenerators.GeneratePerimeterBlock(pCurve, currentSetback, currentDepth);
                        blockFootprints = result.Item1;
                        
                        // Fallback to Linear if Courtyard generation failed (e.g. too small)
                        if (blockFootprints.Count == 0)
                        {
                            blockFootprints = BuildingGenerators.GenerateLinearBlock(pCurve, currentSetback, currentDepth);
                        }
                        else
                        {
                            courtyardInteriors = result.Item2;
                        }
                        break;
                }

                if (blockFootprints == null || blockFootprints.Count == 0)
                    continue;

                // Apply corner radius if specified
                if (selectedConfig.CornerRadius > 0.01)
                {
                    var roundedFootprints = new List<Curve>();
                    foreach (var fp in blockFootprints)
                    {
                        var rounded = Curve.CreateFilletCornersCurve(fp, selectedConfig.CornerRadius, 0.001, 0.001);
                        if (rounded != null)
                            roundedFootprints.Add(rounded);
                        else
                            roundedFootprints.Add(fp); // Fallback to original if fillet fails
                    }
                    blockFootprints = roundedFootprints;
                }

                footprints.AddRange(blockFootprints);
                
                // Collect courtyard boundaries for output
                if (courtyardInteriors != null && courtyardInteriors.Count > 0)
                {
                    courtyards.AddRange(courtyardInteriors);
                }

                // Random height based on selected config
                double fMin = Math.Max(1.0, selectedConfig.MinFloors);
                double fMax = Math.Max(fMin, selectedConfig.MaxFloors);
                double avgFloors = rng.NextDouble() * (fMax - fMin) + fMin;
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
