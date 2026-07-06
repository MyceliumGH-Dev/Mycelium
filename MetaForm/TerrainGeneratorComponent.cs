using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace MetaForm
{
    /// <summary>
    /// Terrain Generator Component - Creates procedural terrain surfaces within a boundary curve
    /// Uses OpenSimplex noise and Ridged Multifractal with a damping power curve for peak control
    /// </summary>
    public class TerrainGeneratorComponent : GH_Component
    {
        public TerrainGeneratorComponent()
          : base("Terrain Generator", "Terrain",
              "Generates organic terrain with adjustable peak sharpness and damping",
              "FormFlux", "Terrain")
        { }

        public override Guid ComponentGuid => new Guid("7A8B9C0D-1E2F-4A5B-6C7D-8E9F0A1B2C3D");
        protected override Bitmap Icon => null; // TODO: Add terrain icon

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundary", "B", "Closed curve that defines the terrain outline", GH_ParamAccess.item);
            pManager.AddNumberParameter("Resolution", "R", "Grid cell size ΓÇö smaller values give more detail but run slower (try 1 to 10)", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("BaseHeight", "Hb", "Base ground level ΓÇö the terrain sits on top of this", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("MaxHeight", "H", "Maximum terrain height in model units (try 5 to 50)", GH_ParamAccess.item, 20.0);
            pManager.AddNumberParameter("NoiseScale", "NS", "Horizontal scale of hills ΓÇö smaller values create broader hills, larger values create tighter bumps", GH_ParamAccess.item, 0.05);
            pManager.AddIntegerParameter("Seed", "S", "Random seed ΓÇö same number always produces the same terrain shape", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Damping", "D", "Peak sharpness ΓÇö below 1 smooths and rounds peaks, 1 is raw noise, above 1 sharpens peaks and flattens valleys (try 0.3 to 3.0)", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Terrain", "T", "Generated terrain surface", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get inputs
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

            // Validate inputs
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

            // Generate terrain
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
        /// Creates terrain surface within boundary using OpenSimplex noise, Ridged Multifractal,
        /// and a damping power curve for peak/valley control
        /// </summary>
        private Brep CreateTerrain(Curve boundary, double resolution, double baseHeight,
            double maxHeight, double noiseScale, int seed, double damping)
        {
            // Get bounding box
            BoundingBox bbox = boundary.GetBoundingBox(true);
            if (!bbox.IsValid) return null;

            // Add padding to ensure terrain fully covers the boundary
            double padding = resolution * 4;
            Point3d minPt = new Point3d(bbox.Min.X - padding, bbox.Min.Y - padding, 0);
            Point3d maxPt = new Point3d(bbox.Max.X + padding, bbox.Max.Y + padding, 0);

            double width = maxPt.X - minPt.X;
            double height = maxPt.Y - minPt.Y;

            if (width <= 0 || height <= 0) return null;

            // Calculate grid dimensions
            int cols = (int)(width / resolution) + 1;
            int rows = (int)(height / resolution) + 1;

            // Ensure minimum grid density
            int minSamples = 20;
            if (cols < minSamples) cols = minSamples;
            if (rows < minSamples) rows = minSamples;

            if (cols < 2 || rows < 2) return null;

            // Initialize OpenSimplex noise generator (isotropic, no grid bias)
            OpenSimplexNoise noise = new OpenSimplexNoise(seed);

            // Generate a pseudo-random coordinate offset based on the seed
            // to prevent the zero-point anomaly at the coordinate origin (0,0)
            double seedOffsetX = (seed * 1234.5678) % 10000.0;
            double seedOffsetY = (seed * 7890.1234) % 10000.0;

            // Generate heightmap using Ridged Multifractal
            double[,] heightmap = new double[rows, cols];

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    double offsetX = resolution * 0.13;
                    double offsetY = resolution * 0.17;
                    double x = minPt.X + j * resolution + offsetX;
                    double y = minPt.Y + i * resolution + offsetY;

                    // Offset coordinates for noise evaluation to avoid origin artifacts
                    double noiseX = (x + seedOffsetX) * noiseScale;
                    double noiseY = (y + seedOffsetY) * noiseScale;

                    // Apply domain warp for organic terrain features
                    Point2d warped = DomainWarp(noise, noiseX, noiseY, 1.5);
                    double nx = warped.X * 0.1;
                    double ny = warped.Y * 0.1;

                    // Calculate raw height using Ridged Multifractal
                    double noiseValue = RidgedMultifractal(noise, nx, ny);
                    heightmap[i, j] = noiseValue; // Raw [0, 1] noise; damping applied next
                }
            }

            // Apply damping and height scaling
            ApplyDamping(heightmap, rows, cols, baseHeight, maxHeight, damping);

            // Build NURBS surface from heightmap
            List<Point3d> points = new List<Point3d>();
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    double offsetX = resolution * 0.13;
                    double offsetY = resolution * 0.17;
                    double x = minPt.X + j * resolution + offsetX;
                    double y = minPt.Y + i * resolution + offsetY;
                    double z = heightmap[i, j];
                    points.Add(new Point3d(x, y, z));
                }
            }

            // Create NURBS surface
            NurbsSurface surface = NurbsSurface.CreateFromPoints(points, rows, cols, 1, 1);
            if (surface == null) return null;

            Brep brep = surface.ToBrep();
            if (brep == null) return null;

            // Trim terrain to boundary
            Brep trimmedTerrain = TrimTerrainToBoundary(brep, boundary);
            
            return trimmedTerrain ?? brep;
        }

        /// <summary>
        /// Applies damping power curve and height scaling to raw noise heightmap.
        /// Damping below 1.0 smooths and rounds peaks (square root territory).
        /// Damping of 1.0 leaves the raw noise unchanged.
        /// Damping above 1.0 sharpens peaks and flattens valleys (squaring/cubing territory).
        /// </summary>
        private void ApplyDamping(double[,] heightmap, int rows, int cols,
            double baseHeight, double maxHeight, double damping)
        {
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    // Clamp raw noise to [0, 1]
                    double raw = Math.Max(0.0, Math.Min(1.0, heightmap[i, j]));

                    // Apply damping power curve: raw ^ damping
                    // damping < 1 = smooth/round peaks, damping > 1 = sharp peaks + flat valleys
                    double shaped = Math.Pow(raw, damping);

                    // Scale to physical height and apply base offset
                    heightmap[i, j] = shaped * maxHeight + baseHeight;
                }
            }
        }

        /// <summary>
        /// Trims terrain surface to boundary using curve projection (no 3D Boolean)
        /// Projects boundary onto terrain along Z-axis and splits the surface
        /// </summary>
        private Brep TrimTerrainToBoundary(Brep terrain, Curve boundary)
        {
            double tolerance = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;

            // Ensure boundary is closed
            if (!boundary.IsClosed)
            {
                boundary = boundary.DuplicateCurve();
                boundary.MakeClosed(tolerance);
                if (!boundary.IsClosed) return terrain;
            }

            if (!boundary.IsValid) return terrain;

            // Step 1: Flatten boundary curve onto XY plane
            Curve flatBoundary = boundary.DuplicateCurve();
            Transform flatten = Transform.PlanarProjection(Plane.WorldXY);
            flatBoundary.Transform(flatten);

            // Step 2: Project boundary curve onto terrain Brep along Z-axis
            Curve[] projectedCurves = Curve.ProjectToBrep(
                flatBoundary, terrain, Vector3d.ZAxis, tolerance);

            if (projectedCurves == null || projectedCurves.Length == 0)
            {
                // Fallback: try negative Z direction
                projectedCurves = Curve.ProjectToBrep(
                    flatBoundary, terrain, -Vector3d.ZAxis, tolerance);
            }

            if (projectedCurves == null || projectedCurves.Length == 0)
                return terrain;

            // Step 3: Join projected curve segments into a single closed curve
            Curve[] joined = Curve.JoinCurves(projectedCurves, tolerance);
            if (joined == null || joined.Length == 0) return terrain;

            // Find the closed joined curve (prefer closed, fallback to longest)
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

            // Step 4: Split terrain face using the projected curve
            if (terrain.Faces.Count == 0) return terrain;

            Brep splitBrep = terrain.Faces[0].Split(
                new Curve[] { splittingCurve }, tolerance);

            if (splitBrep == null || splitBrep.Faces.Count <= 1) return terrain;

            // Step 5: Select interior piece by finding a point guaranteed to be on the trimmed face
            // A centroid can fall outside a C-shape or ring-shape. Instead, we sample the UV domain
            // to find a point strictly inside the trim boundaries.
            foreach (BrepFace face in splitBrep.Faces)
            {
                Interval uDom = face.Domain(0);
                Interval vDom = face.Domain(1);
                Point3d? validPoint = null;

                // Sample a small grid to find an active point on the face
                for (int uStep = 1; uStep <= 5 && validPoint == null; uStep++)
                {
                    double u = uDom.Min + (uDom.Max - uDom.Min) * (uStep / 6.0);
                    for (int vStep = 1; vStep <= 5 && validPoint == null; vStep++)
                    {
                        double v = vDom.Min + (vDom.Max - vDom.Min) * (vStep / 6.0);
                        if (face.IsPointOnFace(u, v) == Rhino.Geometry.PointFaceRelation.Interior)
                        {
                            validPoint = face.PointAt(u, v);
                        }
                    }
                }

                if (validPoint == null) continue;

                // Flatten point to XY plane for 2D containment test
                Point3d testPt2D = new Point3d(validPoint.Value.X, validPoint.Value.Y, 0);
                PointContainment containment = boundary.Contains(testPt2D, Plane.WorldXY, tolerance);

                if (containment == PointContainment.Inside)
                {
                    Brep faceDup = face.DuplicateFace(true);
                    faceDup.Faces[0].ShrinkFace(BrepFace.ShrinkDisableSide.ShrinkAllSides);
                    return faceDup;
                }
            }

            // Fallback: return face with smallest bounding box diagonal (likely interior)
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
        /// Ridged Multifractal noise (Musgrave et al., 1989)
        /// Creates sharp ridges at noise zero-crossings and flat sediment-filled valleys.
        /// Unlike standard fBm which produces uniform "lumpy" terrain, ridged multifractal
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

                // Create ridges at zero-crossings: val = (offset - |signal|)^sharpness
                signal = offset - Math.Abs(signal);
                signal = Math.Pow(Math.Max(0.0, signal), sharpness);

                // Inter-octave feedback: weight high-frequency detail by previous octave's signal
                signal *= weight;
                weight = Math.Max(0.0, Math.Min(1.0, signal * gain));

                // Accumulate with spectral weighting
                total += signal * spectralWeights[i];

                frequency *= lacunarity;
            }

            // Normalize to [0, 1] range
            return Math.Max(0.0, Math.Min(1.0, total * 0.5));
        }

        /// <summary>
        /// Domain warping for more organic terrain features
        /// </summary>
        private Point2d DomainWarp(OpenSimplexNoise noise, double x, double y, double strength)
        {
            double q = noise.Evaluate(x * 0.02, y * 0.02);
            double r = noise.Evaluate(x * 0.02 + 5.3 * q, y * 0.02 + 4.1 * q);
            return new Point2d(x + strength * q, y + strength * r);
        }
    }

    /// <summary>
    /// OpenSimplex Noise Generator (K.S. Peterson, 2014)
    /// Patent-free noise evaluated on a simplicial (triangular) grid for mathematical isotropy.
    /// Unlike Perlin noise which evaluates on a Cartesian grid and exhibits directional artifacts,
    /// OpenSimplex distributes gradients on a triangular lattice, ensuring the noise field has
    /// no preferred direction.
    /// </summary>
    public class OpenSimplexNoise
    {
        // Skew and unskew constants for 2D simplex grid
        // STRETCH: maps square grid ΓåÆ simplicial grid
        // SQUISH:  maps simplicial grid ΓåÆ square grid
        private const double STRETCH_2D = -0.211324865405187;  // (1/sqrt(2+1)-1)/2
        private const double SQUISH_2D = 0.366025403784439;    // (sqrt(2+1)-1)/2
        private const double NORM_2D = 47.0;

        private short[] perm;
        private short[] permGradIndex2D;

        // 8 gradient directions evenly spaced on the unit circle
        private static readonly sbyte[] gradients2D = {
             5,  2,    2,  5,   -5,  2,   -2,  5,
             5, -2,    2, -5,   -5, -2,   -2, -5,
        };

        public OpenSimplexNoise(int seed)
        {
            perm = new short[256];
            permGradIndex2D = new short[256];
            short[] source = new short[256];
            for (short i = 0; i < 256; i++)
                source[i] = i;

            // LCG-based seeding (standard OpenSimplex approach)
            long s = (long)seed;
            s = s * 6364136223846793005L + 1442695040888963407L;
            s = s * 6364136223846793005L + 1442695040888963407L;
            s = s * 6364136223846793005L + 1442695040888963407L;

            for (int i = 255; i >= 0; i--)
            {
                s = s * 6364136223846793005L + 1442695040888963407L;
                int r = (int)((s + 31) % (i + 1));
                if (r < 0) r += (i + 1);
                perm[i] = source[r];
                permGradIndex2D[i] = (short)((perm[i] % (gradients2D.Length / 2)) * 2);
                source[r] = source[i];
            }
        }

        /// <summary>
        /// Evaluate 2D OpenSimplex noise at coordinates (x, y).
        /// Returns a value in approximately [-1, 1].
        /// </summary>
        public double Evaluate(double x, double y)
        {
            // Stretch input space to determine which simplex cell we're in
            double stretchOffset = (x + y) * STRETCH_2D;
            double xs = x + stretchOffset;
            double ys = y + stretchOffset;

            // Floor to get base simplex cell coordinates
            int xsb = FastFloor(xs);
            int ysb = FastFloor(ys);

            // Squish back to get position relative to cell origin in real space
            double squishOffset = (xsb + ysb) * SQUISH_2D;
            double xb = xsb + squishOffset;
            double yb = ysb + squishOffset;

            // Fractional position within the stretched cell
            double xins = xs - xsb;
            double yins = ys - ysb;

            // Sum to determine which simplex triangle we're in
            double inSum = xins + yins;

            // Position relative to the cell origin in real space
            double dx0 = x - xb;
            double dy0 = y - yb;

            double value = 0;

            // Contribution from (0, 0)
            double attn0 = 2.0 - dx0 * dx0 - dy0 * dy0;
            if (attn0 > 0)
            {
                attn0 *= attn0;
                value += attn0 * attn0 * Extrapolate(xsb, ysb, dx0, dy0);
            }

            // Contribution from (1, 0)
            double dx1 = dx0 - 1.0 - SQUISH_2D;
            double dy1 = dy0 - SQUISH_2D;
            double attn1 = 2.0 - dx1 * dx1 - dy1 * dy1;
            if (attn1 > 0)
            {
                attn1 *= attn1;
                value += attn1 * attn1 * Extrapolate(xsb + 1, ysb, dx1, dy1);
            }

            // Contribution from (0, 1)
            double dx2 = dx0 - SQUISH_2D;
            double dy2 = dy0 - 1.0 - SQUISH_2D;
            double attn2 = 2.0 - dx2 * dx2 - dy2 * dy2;
            if (attn2 > 0)
            {
                attn2 *= attn2;
                value += attn2 * attn2 * Extrapolate(xsb, ysb + 1, dx2, dy2);
            }

            // Determine extra vertex based on which simplex triangle we're in
            double dx_ext, dy_ext;
            int xsv_ext, ysv_ext;

            if (inSum <= 1.0)
            {
                // Triangle (0,0)-(1,0)-(0,1): closer to origin
                double zins = 1.0 - inSum;
                if (zins > xins || zins > yins)
                {
                    if (xins > yins)
                    {
                        xsv_ext = xsb + 1;
                        ysv_ext = ysb - 1;
                        dx_ext = dx0 - 1.0;
                        dy_ext = dy0 + 1.0;
                    }
                    else
                    {
                        xsv_ext = xsb - 1;
                        ysv_ext = ysb + 1;
                        dx_ext = dx0 + 1.0;
                        dy_ext = dy0 - 1.0;
                    }
                }
                else
                {
                    xsv_ext = xsb + 1;
                    ysv_ext = ysb + 1;
                    dx_ext = dx0 - 1.0 - 2.0 * SQUISH_2D;
                    dy_ext = dy0 - 1.0 - 2.0 * SQUISH_2D;
                }
            }
            else
            {
                // Triangle (1,0)-(0,1)-(1,1): closer to (1,1)
                double zins = 2.0 - inSum;
                if (zins < xins || zins < yins)
                {
                    if (xins > yins)
                    {
                        xsv_ext = xsb + 2;
                        ysv_ext = ysb;
                        dx_ext = dx0 - 2.0 - 2.0 * SQUISH_2D;
                        dy_ext = dy0 - 2.0 * SQUISH_2D;
                    }
                    else
                    {
                        xsv_ext = xsb;
                        ysv_ext = ysb + 2;
                        dx_ext = dx0 - 2.0 * SQUISH_2D;
                        dy_ext = dy0 - 2.0 - 2.0 * SQUISH_2D;
                    }
                }
                else
                {
                    xsv_ext = xsb;
                    ysv_ext = ysb;
                    dx_ext = dx0;
                    dy_ext = dy0;
                }
                xsb += 1;
                ysb += 1;
                dx0 = dx0 - 1.0 - 2.0 * SQUISH_2D;
                dy0 = dy0 - 1.0 - 2.0 * SQUISH_2D;
            }

            // Contribution from extra vertex
            double attn_ext = 2.0 - dx_ext * dx_ext - dy_ext * dy_ext;
            if (attn_ext > 0)
            {
                attn_ext *= attn_ext;
                value += attn_ext * attn_ext * Extrapolate(xsv_ext, ysv_ext, dx_ext, dy_ext);
            }

            return value / NORM_2D;
        }

        private double Extrapolate(int xsb, int ysb, double dx, double dy)
        {
            int index = permGradIndex2D[(perm[xsb & 0xFF] + ysb) & 0xFF];
            return gradients2D[index] * dx + gradients2D[index + 1] * dy;
        }

        private static int FastFloor(double x)
        {
            int xi = (int)x;
            return x < xi ? xi - 1 : xi;
        }
    }
}
