using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mycelium.Core;

namespace Mycelium.Components
{
    /// <summary>
    /// Creates a procedural terrain surface within a boundary curve using OpenSimplex
    /// noise and ridged multifractal fBm, with a damping power curve for peak control.
    /// </summary>
    public class TerrainGeneratorComponent : GH_Component
    {
        public TerrainGeneratorComponent()
          : base("Terrain Generator", "Terrain",
              "Generates organic terrain with adjustable peak sharpness and damping",
              "Mycelium", "Site")
        { }

        // GUID predates the Mycelium rename; existing Grasshopper files depend on it.
        public override Guid ComponentGuid => new Guid("7A8B9C0D-1E2F-4A5B-6C7D-8E9F0A1B2C3D");

        protected override Bitmap Icon => ComponentIcons.Get("MyceliumTerrain");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundary", "B", "Closed curve that defines the terrain outline", GH_ParamAccess.item);
            pManager.AddNumberParameter("Resolution", "R", "Grid cell size - smaller values give more detail but run slower (try 1 to 10)", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("BaseHeight", "Hb", "Base ground level - the terrain sits on top of this", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("MaxHeight", "H", "Maximum terrain height in model units (try 5 to 50)", GH_ParamAccess.item, 20.0);
            pManager.AddNumberParameter("NoiseScale", "NS", "Horizontal scale of hills - smaller values create broader hills, larger values create tighter bumps", GH_ParamAccess.item, 0.05);
            pManager.AddIntegerParameter("Seed", "S", "Random seed - same number always produces the same terrain shape", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Damping", "D", "Peak sharpness - below 1 smooths and rounds peaks, 1 is raw noise, above 1 sharpens peaks and flattens valleys (try 0.3 to 3.0)", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Terrain", "T", "Generated terrain surface", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve boundary = null;
            double resolution = 5.0;
            double baseHeight = 0.0;
            double maxHeight = 20.0;
            double noiseScale = 0.05;
            int seed = 0;
            double damping = 1.0;

            if (!DA.GetData(0, ref boundary)) return;
            if (!DA.GetData(1, ref resolution)) return;
            if (!DA.GetData(2, ref baseHeight)) return;
            if (!DA.GetData(3, ref maxHeight)) return;
            if (!DA.GetData(4, ref noiseScale)) return;
            if (!DA.GetData(5, ref seed)) return;
            if (!DA.GetData(6, ref damping)) return;

            if (boundary == null || !boundary.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid boundary curve");
                return;
            }

            if (resolution <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Resolution must be positive");
                return;
            }

            if (maxHeight <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Max Height must be positive");
                return;
            }

            if (damping <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Damping must be positive");
                return;
            }

            try
            {
                Brep terrain = CreateTerrain(boundary, resolution, baseHeight, maxHeight, noiseScale, seed, damping);

                if (terrain == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to generate terrain");
                    return;
                }

                DA.SetData(0, terrain);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates the terrain surface: samples a ridged-multifractal heightmap over a padded
        /// grid, lofts a NURBS surface through the points, and trims it to the boundary.
        /// </summary>
        private Brep CreateTerrain(Curve boundary, double resolution, double baseHeight,
            double maxHeight, double noiseScale, int seed, double damping)
        {
            BoundingBox bbox = boundary.GetBoundingBox(true);
            if (!bbox.IsValid) return null;

            // Padding ensures the surface fully covers the boundary before trimming
            double padding = resolution * 4;
            Point3d minPt = new Point3d(bbox.Min.X - padding, bbox.Min.Y - padding, 0);
            Point3d maxPt = new Point3d(bbox.Max.X + padding, bbox.Max.Y + padding, 0);

            double width = maxPt.X - minPt.X;
            double height = maxPt.Y - minPt.Y;

            if (width <= 0 || height <= 0) return null;

            int cols = (int)(width / resolution) + 1;
            int rows = (int)(height / resolution) + 1;

            // Ensure minimum grid density
            int minSamples = 20;
            if (cols < minSamples) cols = minSamples;
            if (rows < minSamples) rows = minSamples;

            if (cols < 2 || rows < 2) return null;

            var noise = new OpenSimplexNoise(seed);

            // Seed-dependent coordinate offset prevents the zero-point anomaly at the origin
            double seedOffsetX = (seed * 1234.5678) % 10000.0;
            double seedOffsetY = (seed * 7890.1234) % 10000.0;

            double[,] heightmap = new double[rows, cols];

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    double offsetX = resolution * 0.13;
                    double offsetY = resolution * 0.17;
                    double x = minPt.X + j * resolution + offsetX;
                    double y = minPt.Y + i * resolution + offsetY;

                    double noiseX = (x + seedOffsetX) * noiseScale;
                    double noiseY = (y + seedOffsetY) * noiseScale;

                    // Domain warp gives the ridges an organic, meandering character
                    Point2d warped = DomainWarp(noise, noiseX, noiseY, 1.5);
                    double nx = warped.X * 0.1;
                    double ny = warped.Y * 0.1;

                    heightmap[i, j] = RidgedMultifractal(noise, nx, ny); // raw [0, 1]; damping applied next
                }
            }

            ApplyDamping(heightmap, rows, cols, baseHeight, maxHeight, damping);

            var points = new List<Point3d>();
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    double offsetX = resolution * 0.13;
                    double offsetY = resolution * 0.17;
                    double x = minPt.X + j * resolution + offsetX;
                    double y = minPt.Y + i * resolution + offsetY;
                    points.Add(new Point3d(x, y, heightmap[i, j]));
                }
            }

            NurbsSurface surface = NurbsSurface.CreateFromPoints(points, rows, cols, 1, 1);
            if (surface == null) return null;

            Brep brep = surface.ToBrep();
            if (brep == null) return null;

            Brep trimmedTerrain = TrimTerrainToBoundary(brep, boundary);
            return trimmedTerrain ?? brep;
        }

        /// <summary>
        /// Applies the damping power curve and height scaling to the raw noise heightmap.
        /// Damping below 1.0 smooths and rounds peaks; 1.0 leaves the raw noise unchanged;
        /// above 1.0 sharpens peaks and flattens valleys.
        /// </summary>
        private void ApplyDamping(double[,] heightmap, int rows, int cols,
            double baseHeight, double maxHeight, double damping)
        {
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    double raw = Math.Max(0.0, Math.Min(1.0, heightmap[i, j]));
                    double shaped = Math.Pow(raw, damping);
                    heightmap[i, j] = shaped * maxHeight + baseHeight;
                }
            }
        }

        /// <summary>
        /// Trims the terrain surface to the boundary by projecting the boundary onto the
        /// surface along Z and splitting the face (no 3D boolean involved).
        /// </summary>
        private Brep TrimTerrainToBoundary(Brep terrain, Curve boundary)
        {
            double tolerance = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;

            if (!boundary.IsClosed)
            {
                boundary = boundary.DuplicateCurve();
                boundary.MakeClosed(tolerance);
                if (!boundary.IsClosed) return terrain;
            }

            if (!boundary.IsValid) return terrain;

            // Step 1: flatten the boundary onto the XY plane
            Curve flatBoundary = boundary.DuplicateCurve();
            Transform flatten = Transform.PlanarProjection(Plane.WorldXY);
            flatBoundary.Transform(flatten);

            // Step 2: project the boundary onto the terrain along Z
            Curve[] projectedCurves = Curve.ProjectToBrep(
                flatBoundary, terrain, Vector3d.ZAxis, tolerance);

            if (projectedCurves == null || projectedCurves.Length == 0)
            {
                projectedCurves = Curve.ProjectToBrep(
                    flatBoundary, terrain, -Vector3d.ZAxis, tolerance);
            }

            if (projectedCurves == null || projectedCurves.Length == 0)
                return terrain;

            // Step 3: join the projected segments into one closed splitting curve
            Curve[] joined = Curve.JoinCurves(projectedCurves, tolerance);
            if (joined == null || joined.Length == 0) return terrain;

            Curve splittingCurve = null;
            foreach (Curve c in joined)
            {
                if (c.IsClosed) { splittingCurve = c; break; }
            }
            if (splittingCurve == null)
            {
                splittingCurve = joined.OrderByDescending(c => c.GetLength()).First();
                if (!splittingCurve.IsClosed)
                    splittingCurve.MakeClosed(tolerance * 10);
            }

            if (splittingCurve == null || !splittingCurve.IsValid) return terrain;

            // Step 4: split the terrain face with the projected curve
            if (terrain.Faces.Count == 0) return terrain;

            Brep splitBrep = terrain.Faces[0].Split(
                new Curve[] { splittingCurve }, tolerance);

            if (splitBrep == null || splitBrep.Faces.Count <= 1) return terrain;

            // Step 5: pick the face that lies inside the boundary. A centroid can fall outside
            // a C- or ring-shaped face, so sample the UV domain for a point on the face instead.
            foreach (BrepFace face in splitBrep.Faces)
            {
                Interval uDom = face.Domain(0);
                Interval vDom = face.Domain(1);
                Point3d? validPoint = null;

                for (int uStep = 1; uStep <= 5 && validPoint == null; uStep++)
                {
                    double u = uDom.Min + (uDom.Max - uDom.Min) * (uStep / 6.0);
                    for (int vStep = 1; vStep <= 5 && validPoint == null; vStep++)
                    {
                        double v = vDom.Min + (vDom.Max - vDom.Min) * (vStep / 6.0);
                        if (face.IsPointOnFace(u, v) == PointFaceRelation.Interior)
                        {
                            validPoint = face.PointAt(u, v);
                        }
                    }
                }

                if (validPoint == null) continue;

                Point3d testPt2D = new Point3d(validPoint.Value.X, validPoint.Value.Y, 0);
                PointContainment containment = boundary.Contains(testPt2D, Plane.WorldXY, tolerance);

                if (containment == PointContainment.Inside)
                {
                    Brep faceDup = face.DuplicateFace(true);
                    faceDup.Faces[0].ShrinkFace(BrepFace.ShrinkDisableSide.ShrinkAllSides);
                    return faceDup;
                }
            }

            // Fallback: the face with the smallest bounding box diagonal is likely the interior
            BrepFace smallestFace = splitBrep.Faces[0];
            double smallestDiag = double.MaxValue;
            foreach (BrepFace face in splitBrep.Faces)
            {
                Brep faceDup = face.DuplicateFace(true);
                double diag = faceDup.GetBoundingBox(true).Diagonal.Length;
                if (diag < smallestDiag)
                {
                    smallestDiag = diag;
                    smallestFace = face;
                }
            }

            Brep fallback = smallestFace.DuplicateFace(true);
            fallback.Faces[0].ShrinkFace(BrepFace.ShrinkDisableSide.ShrinkAllSides);
            return fallback;
        }

        /// <summary>
        /// Ridged multifractal noise (Musgrave et al., 1989).
        /// Creates sharp ridges at noise zero-crossings and flat sediment-filled valleys.
        /// Unlike standard fBm, which produces uniform "lumpy" terrain, ridged multifractal
        /// uses |signal| inversion and inter-octave feedback to concentrate high-frequency
        /// detail on ridge crests while leaving valleys smooth.
        /// </summary>
        private double RidgedMultifractal(OpenSimplexNoise noise, double x, double y,
            int octaves = 6, double lacunarity = 2.0, double gain = 2.0,
            double offset = 1.0, double H = 0.9, double sharpness = 2.0)
        {
            double total = 0.0;
            double frequency = 1.0;
            double weight = 1.0;

            // Precompute spectral weights: higher octaves contribute less energy
            double[] spectralWeights = new double[octaves];
            double specFreq = 1.0;
            for (int i = 0; i < octaves; i++)
            {
                spectralWeights[i] = Math.Pow(specFreq, -H);
                specFreq *= lacunarity;
            }

            for (int i = 0; i < octaves; i++)
            {
                double signal = noise.Evaluate(x * frequency, y * frequency);

                // Ridges form at zero-crossings: val = (offset - |signal|)^sharpness
                signal = offset - Math.Abs(signal);
                signal = Math.Pow(Math.Max(0.0, signal), sharpness);

                // Inter-octave feedback: weight high-frequency detail by the previous octave
                signal *= weight;
                weight = Math.Max(0.0, Math.Min(1.0, signal * gain));

                total += signal * spectralWeights[i];
                frequency *= lacunarity;
            }

            return Math.Max(0.0, Math.Min(1.0, total * 0.5));
        }

        /// <summary>
        /// Domain warping for more organic terrain features.
        /// </summary>
        private Point2d DomainWarp(OpenSimplexNoise noise, double x, double y, double strength)
        {
            double q = noise.Evaluate(x * 0.02, y * 0.02);
            double r = noise.Evaluate(x * 0.02 + 5.3 * q, y * 0.02 + 4.1 * q);
            return new Point2d(x + strength * q, y + strength * r);
        }
    }
}
