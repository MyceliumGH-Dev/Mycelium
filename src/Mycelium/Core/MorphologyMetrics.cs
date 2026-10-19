using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rhino.Geometry;

namespace Mycelium.Core
{
    /// <summary>
    /// Urban morphology indicators derived from one generated massing case.
    /// Areas are reported in the active Rhino document's squared model units.
    /// </summary>
    public sealed class MorphologyMetricsResult
    {
        public double SiteArea { get; set; }
        public double BuildingPlanArea { get; set; }
        public double ParkArea { get; set; }
        public double PlanAreaDensity { get; set; }
        public double OpenSpaceRatio { get; set; }
        public double ParkAreaRatio { get; set; }
        public double GrossFrontalArea { get; set; }
        public double FrontalAreaDensity { get; set; }
        public double MeanHeight { get; set; }
        public double HeightStandardDeviation { get; set; }
        public double PlanAreaWeightedMeanHeight { get; set; }
        public double PlanAreaWeightedHeightStandardDeviation { get; set; }
        public double MinimumHeight { get; set; }
        public double MaximumHeight { get; set; }
        public double MedianHeight { get; set; }
        public double HeightP90 { get; set; }
        public double AnalysisDirectionX { get; set; }
        public double AnalysisDirectionY { get; set; }
        public int BuildingFootprintCount { get; set; }
        public int MassCount { get; set; }
        public double BuiltVolume { get; set; }
        public double BuiltDensityRatio { get; set; }
        public double RoadArea { get; set; }
        public double RoadAreaRatio { get; set; }
        public double FacadeArea { get; set; }
        public double VerticalAreaRatio { get; set; }
        public double SkyViewFactor { get; set; }
        public int SkyViewFactorSampleCount { get; set; }
        public double AspectRatio { get; set; }

        public string ToDisplayString()
        {
            var text = new StringBuilder();
            text.AppendLine("--- Morphology Metrics ---");
            text.AppendLine($"Built Coverage Ratio (BCR, λp): {PlanAreaDensity:F3}");
            text.AppendLine($"Built Density Ratio (BDR): {BuiltDensityRatio:F3}");
            text.AppendLine($"Road Area Ratio (RaR): {RoadAreaRatio:F3}");
            text.AppendLine($"Vertical Area Ratio (VR): {VerticalAreaRatio:F3}");
            text.AppendLine($"Sky View Factor (SVF, ground mean, {SkyViewFactorSampleCount} points): {SkyViewFactor:F3}");
            text.AppendLine($"Aspect Ratio (AR, H/W): {AspectRatio:F3}");
            text.AppendLine($"Open Space Ratio: {OpenSpaceRatio:F3}");
            text.AppendLine($"Park Area Ratio: {ParkAreaRatio:F3}");
            text.AppendLine($"Frontal Area Density (λf, projected, unshielded): {FrontalAreaDensity:F3}");
            text.AppendLine($"Analysis Direction: ({AnalysisDirectionX:F3}, {AnalysisDirectionY:F3})");
            text.AppendLine($"Mean Height: {MeanHeight:F2}");
            text.AppendLine($"Height Std. Dev. (σH): {HeightStandardDeviation:F2}");
            text.AppendLine($"Building Height (BHt, plan-area-weighted mean): {PlanAreaWeightedMeanHeight:F2}");
            text.AppendLine($"Plan-Area-Weighted σH: {PlanAreaWeightedHeightStandardDeviation:F2}");
            text.AppendLine($"Height Min / Median / P90 / Max: {MinimumHeight:F2} / {MedianHeight:F2} / {HeightP90:F2} / {MaximumHeight:F2}");
            text.AppendLine($"Building Plan Area: {BuildingPlanArea:F2}");
            text.AppendLine($"Park Area: {ParkArea:F2}");
            text.AppendLine($"Road Area: {RoadArea:F2}");
            text.AppendLine($"Facade Area: {FacadeArea:F2}");
            text.AppendLine($"Built Volume: {BuiltVolume:F2}");
            text.Append($"Gross Frontal Area: {GrossFrontalArea:F2}");
            return text.ToString();
        }
    }

    public static class MorphologyMetrics
    {
        internal sealed class HeightStatistics
        {
            public double Mean { get; set; }
            public double StandardDeviation { get; set; }
            public double Minimum { get; set; }
            public double Maximum { get; set; }
            public double Median { get; set; }
            public double P90 { get; set; }
        }

        /// <summary>Target number of ground sample points for the sky view factor.</summary>
        internal const int SkyViewSampleTarget = 400;

        /// <summary>Rays cast per ground sample point for the sky view factor.</summary>
        internal const int SkyViewRayCount = 144;

        /// <param name="massHeights">
        /// Design height of each mass above its pad, index-aligned with <paramref name="masses"/>.
        /// On terrain a mass also extends below ground as a foundation, so the height of the solid
        /// overstates the building; every height-based metric uses these values instead. When
        /// null, the vertical extent of each solid is used, which is exact on a flat site.
        /// </param>
        /// <param name="allParcels">
        /// Every planar parcel the subdivision produced, park parcels included. Streets are the
        /// site left over once these are removed, so road area is site area minus their sum.
        /// </param>
        /// <param name="streetWidth">Canyon width used for the aspect ratio.</param>
        /// <param name="terrain">Optional terrain; sky view samples sit on it and it occludes.</param>
        public static MorphologyMetricsResult Calculate(Curve boundary, IReadOnlyList<Curve> footprints,
            IReadOnlyList<Brep> masses, IReadOnlyList<double> massHeights, IReadOnlyList<Curve> parks,
            IReadOnlyList<Curve> allParcels, double streetWidth, Brep terrain, Vector3d analysisDirection)
        {
            double siteArea = GeometryHelpers.GetCurveArea(boundary);
            double planArea = GeometryHelpers.GetRegionArea(footprints);
            double parkArea = SumCurveAreas(parks);

            var direction = new Vector3d(analysisDirection.X, analysisDirection.Y, 0.0);
            if (!direction.Unitize())
                direction = Vector3d.XAxis;

            var measures = CollectMassMeasures(masses, massHeights, direction);
            var weightedHeights = measures.Select(m => new WeightedHeight(m.Height, m.PlanArea)).ToList();
            var validHeights = weightedHeights.Select(entry => entry.Height).OrderBy(h => h).ToList();

            var heightStatistics = CalculateHeightStatistics(validHeights);
            var weightedStatistics = CalculateWeightedHeightStatistics(weightedHeights);
            double grossFrontalArea = measures.Sum(m => m.FrontalArea);

            double builtVolume = measures.Sum(m => m.BuiltVolume);
            double roadArea = Math.Max(0.0, siteArea - SumCurveAreas(allParcels));
            double facadeArea = measures.Sum(m => m.FacadeArea);
            double skyViewFactor = CalculateSkyViewFactor(boundary, siteArea, masses, terrain,
                out int skyViewSamples);

            return new MorphologyMetricsResult
            {
                SiteArea = siteArea,
                BuildingPlanArea = planArea,
                ParkArea = parkArea,
                PlanAreaDensity = SafeRatio(planArea, siteArea),
                OpenSpaceRatio = Math.Max(0.0, 1.0 - SafeRatio(planArea, siteArea)),
                ParkAreaRatio = SafeRatio(parkArea, siteArea),
                GrossFrontalArea = grossFrontalArea,
                FrontalAreaDensity = SafeRatio(grossFrontalArea, siteArea),
                MeanHeight = heightStatistics.Mean,
                HeightStandardDeviation = heightStatistics.StandardDeviation,
                PlanAreaWeightedMeanHeight = weightedStatistics.Mean,
                PlanAreaWeightedHeightStandardDeviation = weightedStatistics.StandardDeviation,
                MinimumHeight = heightStatistics.Minimum,
                MaximumHeight = heightStatistics.Maximum,
                MedianHeight = heightStatistics.Median,
                HeightP90 = heightStatistics.P90,
                AnalysisDirectionX = direction.X,
                AnalysisDirectionY = direction.Y,
                BuildingFootprintCount = footprints?.Count ?? 0,
                MassCount = masses?.Count ?? 0,
                BuiltVolume = builtVolume,
                BuiltDensityRatio = SafeRatio(builtVolume, siteArea),
                RoadArea = roadArea,
                RoadAreaRatio = SafeRatio(roadArea, siteArea),
                FacadeArea = facadeArea,
                VerticalAreaRatio = SafeRatio(facadeArea, siteArea),
                SkyViewFactor = skyViewFactor,
                SkyViewFactorSampleCount = skyViewSamples,
                // Mean of H / W over buildings; W is the one street width the generator uses.
                AspectRatio = SafeRatio(heightStatistics.Mean, streetWidth)
            };
        }

        internal readonly struct WeightedHeight
        {
            public WeightedHeight(double height, double planArea)
            {
                Height = height;
                PlanArea = planArea;
            }

            public double Height { get; }
            public double PlanArea { get; }
        }

        /// <summary>
        /// Raw measures of one mass and the per-mass metrics derived from them. Every mass is a
        /// vertical prism: on a flat site its solid spans exactly its design height; on terrain
        /// the solid also extends below the pad as a foundation, so <see cref="Extent"/> exceeds
        /// <see cref="Height"/>. Metrics use the design height and count only what stands above
        /// the pad, so they agree with the Heights output.
        /// </summary>
        internal readonly struct MassMeasure
        {
            public MassMeasure(double height, double extent, double volume, double surfaceArea,
                double crosswindWidth)
            {
                Height = height;
                Extent = extent;
                Volume = volume;
                SurfaceArea = surfaceArea;
                CrosswindWidth = crosswindWidth;
            }

            /// <summary>Design height above the pad.</summary>
            public double Height { get; }

            /// <summary>Vertical extent of the solid, foundation included.</summary>
            public double Extent { get; }

            public double Volume { get; }
            public double SurfaceArea { get; }

            /// <summary>Width of the mass across the analysis direction.</summary>
            public double CrosswindWidth { get; }

            /// <summary>
            /// Volume over extrusion height, exact for a prism and independent of how many Breps
            /// one building happened to be split into.
            /// </summary>
            public double PlanArea => Extent > 0.0 ? Volume / Extent : 0.0;

            public double BuiltVolume => PlanArea * Height;

            /// <summary>
            /// Silhouette area seen along the analysis direction: crosswind width times height,
            /// exact for a prism. Summed over masses this is the UNSHIELDED frontal area index of
            /// Grimmond and Oke (1999): masses standing behind one another are both counted in
            /// full, and the interior faces of re-entrant footprints such as U-shapes are not.
            /// </summary>
            public double FrontalArea => CrosswindWidth * Height;

            /// <summary>
            /// Wall area above the pad, courtyard walls included: the solid's surface minus roof
            /// and base gives perimeter times extent, scaled to the design height so that buried
            /// foundation walls are excluded.
            /// </summary>
            public double FacadeArea => Extent > 0.0
                ? FacadeAreaFromSurface(SurfaceArea, PlanArea) * Height / Extent
                : 0.0;
        }

        private static List<MassMeasure> CollectMassMeasures(IReadOnlyList<Brep> masses,
            IReadOnlyList<double> massHeights, Vector3d direction)
        {
            var measures = new List<MassMeasure>();
            if (masses == null)
                return measures;

            var crosswind = new Vector3d(-direction.Y, direction.X, 0.0);
            var analysisPlane = new Plane(Point3d.Origin, direction, crosswind);

            for (int i = 0; i < masses.Count; i++)
            {
                var mass = masses[i];
                if (mass == null)
                    continue;

                var box = mass.GetBoundingBox(true);
                if (!box.IsValid)
                    continue;

                double extent = Math.Max(0.0, box.Max.Z - box.Min.Z);
                if (double.IsNaN(extent) || double.IsInfinity(extent))
                    continue;

                // Capped at the extent so floating-point noise on a flat site cannot push the
                // above-pad fraction past one; a missing or invalid design height falls back to it.
                double height = extent;
                if (massHeights != null && i < massHeights.Count &&
                    massHeights[i] >= 0.0 && !double.IsInfinity(massHeights[i]))
                {
                    height = Math.Min(massHeights[i], extent);
                }

                double volume = 0.0;
                double surfaceArea = 0.0;
                if (extent > 0.0)
                {
                    using (var properties = VolumeMassProperties.Compute(mass))
                    {
                        if (properties != null && !double.IsNaN(properties.Volume) && !double.IsInfinity(properties.Volume))
                            volume = Math.Abs(properties.Volume);
                    }

                    using (var properties = AreaMassProperties.Compute(mass))
                    {
                        if (properties != null && !double.IsNaN(properties.Area) && !double.IsInfinity(properties.Area))
                            surfaceArea = properties.Area;
                    }
                }

                var planeBox = mass.GetBoundingBox(analysisPlane);
                double crosswindWidth = planeBox.IsValid ? Math.Max(0.0, planeBox.Max.Y - planeBox.Min.Y) : 0.0;

                measures.Add(new MassMeasure(height, extent, volume, surfaceArea, crosswindWidth));
            }

            return measures;
        }

        /// <summary>
        /// Plan-area-weighted mean and population standard deviation of building height.
        /// Roughness parameterizations expect the weighted moments; the unweighted values give
        /// a single shed the same influence as a tower. Falls back to unweighted statistics when
        /// no positive weights are available.
        /// </summary>
        internal static HeightStatistics CalculateWeightedHeightStatistics(
            IReadOnlyList<WeightedHeight> entries)
        {
            if (entries == null || entries.Count == 0)
                return new HeightStatistics();

            double totalWeight = entries.Sum(entry => Math.Max(0.0, entry.PlanArea));
            if (totalWeight <= 0.0)
                return CalculateHeightStatistics(entries.Select(entry => entry.Height));

            double mean = entries.Sum(entry => Math.Max(0.0, entry.PlanArea) * entry.Height) / totalWeight;
            double variance = entries.Sum(entry =>
                Math.Max(0.0, entry.PlanArea) * (entry.Height - mean) * (entry.Height - mean)) / totalWeight;

            return new HeightStatistics
            {
                Mean = mean,
                StandardDeviation = Math.Sqrt(Math.Max(0.0, variance))
            };
        }

        internal static HeightStatistics CalculateHeightStatistics(IEnumerable<double> heights)
        {
            var sorted = heights == null
                ? new List<double>()
                : heights.Where(h => !double.IsNaN(h) && !double.IsInfinity(h) && h >= 0.0)
                    .OrderBy(h => h).ToList();
            double mean = sorted.Count > 0 ? sorted.Average() : 0.0;
            double variance = sorted.Count > 0
                ? sorted.Sum(h => (h - mean) * (h - mean)) / sorted.Count
                : 0.0;

            return new HeightStatistics
            {
                Mean = mean,
                StandardDeviation = Math.Sqrt(variance),
                Minimum = sorted.Count > 0 ? sorted[0] : 0.0,
                Maximum = sorted.Count > 0 ? sorted[sorted.Count - 1] : 0.0,
                Median = Percentile(sorted, 0.50),
                P90 = Percentile(sorted, 0.90)
            };
        }

        /// <summary>Wall area of a prism: its surface area minus roof and base.</summary>
        internal static double FacadeAreaFromSurface(double surfaceArea, double planArea)
        {
            return Math.Max(0.0, surfaceArea - 2.0 * Math.Max(0.0, planArea));
        }

        /// <summary>
        /// Mean sky view factor over open ground inside the boundary. Sample points lie on a
        /// regular grid; each casts a fixed cosine-weighted ray set against the masses (and the
        /// terrain, if any), and its SVF is the unobstructed fraction. Points under a mass are
        /// skipped, so the mean covers streets, parks, courtyards and setbacks.
        /// </summary>
        /// <remarks>
        /// This is a geometric SVF for a horizontal plane at ground level. Trees are not
        /// occluders, and buildings outside the boundary do not exist, so points near the edge
        /// read higher than they would in a continuous city.
        /// </remarks>
        private static double CalculateSkyViewFactor(Curve boundary, double siteArea,
            IReadOnlyList<Brep> masses, Brep terrain, out int sampleCount)
        {
            sampleCount = 0;
            if (boundary == null || siteArea <= 0.0)
                return 0.0;

            var massMesh = MeshBreps(masses);
            var terrainMesh = terrain != null ? MeshBreps(new[] { terrain }) : null;
            var rays = CosineWeightedHemisphere(SkyViewRayCount);

            var box = boundary.GetBoundingBox(true);
            var plane = new Plane(new Point3d(0.0, 0.0, box.Min.Z), Vector3d.ZAxis);
            double spacing = Math.Sqrt(siteArea / SkyViewSampleTarget);
            double lift = spacing * 1e-3;
            double skyTop = box.Max.Z;
            if (massMesh != null)
                skyTop = Math.Max(skyTop, massMesh.GetBoundingBox(false).Max.Z);
            if (terrainMesh != null)
                skyTop = Math.Max(skyTop, terrainMesh.GetBoundingBox(false).Max.Z);

            double total = 0.0;
            for (double x = box.Min.X + spacing / 2.0; x < box.Max.X; x += spacing)
            {
                for (double y = box.Min.Y + spacing / 2.0; y < box.Max.Y; y += spacing)
                {
                    var point = new Point3d(x, y, box.Min.Z);
                    if (boundary.Contains(point, plane, 1e-6) != PointContainment.Inside)
                        continue;

                    if (terrainMesh != null)
                    {
                        var down = new Ray3d(new Point3d(x, y, skyTop + spacing), -Vector3d.ZAxis);
                        double hit = Rhino.Geometry.Intersect.Intersection.MeshRay(terrainMesh, down);
                        if (hit < 0.0)
                            continue;
                        point = down.PointAt(hit);
                    }

                    point.Z += lift;

                    // A point under a mass sees a roof straight up; it is not open ground.
                    if (massMesh != null &&
                        Rhino.Geometry.Intersect.Intersection.MeshRay(massMesh, new Ray3d(point, Vector3d.ZAxis)) >= 0.0)
                        continue;

                    int open = 0;
                    foreach (var direction in rays)
                    {
                        var ray = new Ray3d(point, direction);
                        bool blocked =
                            (massMesh != null && Rhino.Geometry.Intersect.Intersection.MeshRay(massMesh, ray) >= 0.0) ||
                            (terrainMesh != null && Rhino.Geometry.Intersect.Intersection.MeshRay(terrainMesh, ray) >= 0.0);
                        if (!blocked)
                            open++;
                    }

                    total += (double)open / rays.Length;
                    sampleCount++;
                }
            }

            return sampleCount > 0 ? total / sampleCount : 0.0;
        }

        /// <summary>
        /// Deterministic cosine-weighted directions over the upper hemisphere (Fibonacci spiral
        /// on the unit disk, lifted to the sphere). With this density every ray carries equal
        /// weight, so the sky view factor is simply the unobstructed fraction.
        /// </summary>
        internal static Vector3d[] CosineWeightedHemisphere(int count)
        {
            var directions = new Vector3d[Math.Max(0, count)];
            double goldenAngle = Math.PI * (3.0 - Math.Sqrt(5.0));
            for (int i = 0; i < directions.Length; i++)
            {
                double u = (i + 0.5) / directions.Length;
                double radius = Math.Sqrt(u);
                double angle = i * goldenAngle;
                directions[i] = new Vector3d(radius * Math.Cos(angle), radius * Math.Sin(angle),
                    Math.Sqrt(1.0 - u));
            }

            return directions;
        }

        private static Mesh MeshBreps(IEnumerable<Brep> breps)
        {
            if (breps == null)
                return null;

            var joined = new Mesh();
            foreach (var brep in breps)
            {
                if (brep == null)
                    continue;

                var pieces = Mesh.CreateFromBrep(brep, MeshingParameters.FastRenderMesh);
                if (pieces == null)
                    continue;

                foreach (var piece in pieces)
                    joined.Append(piece);
            }

            return joined.Faces.Count > 0 ? joined : null;
        }

        private static double SumCurveAreas(IReadOnlyList<Curve> curves)
        {
            if (curves == null)
                return 0.0;

            double sum = 0.0;
            foreach (var curve in curves)
                sum += GeometryHelpers.GetCurveArea(curve);
            return sum;
        }

        private static double SafeRatio(double numerator, double denominator)
        {
            return denominator > 0.0 ? numerator / denominator : 0.0;
        }

        private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
        {
            if (sortedValues == null || sortedValues.Count == 0)
                return 0.0;
            if (sortedValues.Count == 1)
                return sortedValues[0];

            double position = Math.Max(0.0, Math.Min(1.0, percentile)) * (sortedValues.Count - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper)
                return sortedValues[lower];

            double fraction = position - lower;
            return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * fraction;
        }
    }
}
