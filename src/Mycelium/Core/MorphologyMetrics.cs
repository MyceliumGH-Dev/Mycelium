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

        /// <param name="allParcels">
        /// Every planar parcel the subdivision produced, park parcels included. Streets are the
        /// site left over once these are removed, so road area is site area minus their sum.
        /// </param>
        /// <param name="streetWidth">Canyon width used for the aspect ratio.</param>
        /// <param name="terrain">Optional terrain; sky view samples sit on it and it occludes.</param>
        public static MorphologyMetricsResult Calculate(Curve boundary, IReadOnlyList<Curve> footprints,
            IReadOnlyList<Brep> masses, IReadOnlyList<Curve> parks, IReadOnlyList<Curve> allParcels,
            double streetWidth, Brep terrain, Vector3d analysisDirection)
        {
            double siteArea = GeometryHelpers.GetCurveArea(boundary);
            double planArea = GeometryHelpers.GetRegionArea(footprints);
            double parkArea = SumCurveAreas(parks);

            var direction = new Vector3d(analysisDirection.X, analysisDirection.Y, 0.0);
            if (!direction.Unitize())
                direction = Vector3d.XAxis;

            var weightedHeights = CollectWeightedHeights(masses);
            var validHeights = weightedHeights.Select(entry => entry.Height).OrderBy(h => h).ToList();

            var heightStatistics = CalculateHeightStatistics(validHeights);
            var weightedStatistics = CalculateWeightedHeightStatistics(weightedHeights);
            double grossFrontalArea = CalculateGrossFrontalArea(masses, direction);

            double builtVolume = weightedHeights.Sum(entry => entry.PlanArea * entry.Height);
            double roadArea = Math.Max(0.0, siteArea - SumCurveAreas(allParcels));
            double facadeArea = CalculateFacadeArea(masses, weightedHeights);
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
        /// Extracts (height, plan area) per mass. Plan area is recovered as volume / height,
        /// which is exact for the vertical prisms this generator extrudes and keeps the
        /// weighting independent of how many Breps one building happened to be split into.
        /// </summary>
        private static List<WeightedHeight> CollectWeightedHeights(IReadOnlyList<Brep> masses)
        {
            var entries = new List<WeightedHeight>();
            if (masses == null)
                return entries;

            foreach (var mass in masses)
            {
                if (mass == null)
                    continue;

                var box = mass.GetBoundingBox(true);
                if (!box.IsValid)
                    continue;

                double height = Math.Max(0.0, box.Max.Z - box.Min.Z);
                if (double.IsNaN(height) || double.IsInfinity(height))
                    continue;

                double planArea = 0.0;
                if (height > 0.0)
                {
                    using (var volume = VolumeMassProperties.Compute(mass))
                    {
                        if (volume != null && !double.IsNaN(volume.Volume) && !double.IsInfinity(volume.Volume))
                            planArea = Math.Abs(volume.Volume) / height;
                    }
                }

                entries.Add(new WeightedHeight(height, planArea));
            }

            return entries;
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

        /// <summary>
        /// Projected (silhouette) frontal area summed over masses, for the given wind direction.
        /// For the vertical prisms this generator extrudes, the silhouette of one mass is exactly
        /// its crosswind extent times its height, so the oriented bounding box is exact rather
        /// than an approximation.
        /// </summary>
        /// <remarks>
        /// This is the UNSHIELDED sum: masses that stand behind one another are both counted in
        /// full, and no mutual sheltering is applied. It is therefore the frontal area index in
        /// the sense of Grimmond and Oke (1999), not a shielding-aware frontal exposure. It also
        /// differs from the summed windward-face area for re-entrant footprints such as U-shapes,
        /// where interior faces are excluded here.
        /// </remarks>
        private static double CalculateGrossFrontalArea(IReadOnlyList<Brep> masses, Vector3d direction)
        {
            if (masses == null)
                return 0.0;

            if (masses.Count == 0)
                return 0.0;

            var crosswind = new Vector3d(-direction.Y, direction.X, 0.0);
            var analysisPlane = new Plane(Point3d.Origin, direction, crosswind);
            double total = 0.0;

            foreach (var mass in masses)
            {
                if (mass == null)
                    continue;

                var box = mass.GetBoundingBox(analysisPlane);
                if (!box.IsValid)
                    continue;

                double crosswindWidth = Math.Max(0.0, box.Max.Y - box.Min.Y);
                double height = Math.Max(0.0, box.Max.Z - box.Min.Z);
                total += crosswindWidth * height;
            }

            return total;
        }

        /// <summary>
        /// Facade (wall) area summed over masses: perimeter times height. Recovered as total
        /// surface area minus roof and base, which is exact for the vertical prisms this
        /// generator extrudes and counts courtyard walls as facade.
        /// </summary>
        /// <remarks>
        /// On terrain the base of a mass is extended below ground as a foundation, so the
        /// buried part of the wall is included.
        /// </remarks>
        private static double CalculateFacadeArea(IReadOnlyList<Brep> masses,
            IReadOnlyList<WeightedHeight> weightedHeights)
        {
            if (masses == null)
                return 0.0;

            double surfaceArea = 0.0;
            foreach (var mass in masses)
            {
                if (mass == null)
                    continue;

                using (var area = AreaMassProperties.Compute(mass))
                {
                    if (area != null && !double.IsNaN(area.Area) && !double.IsInfinity(area.Area))
                        surfaceArea += area.Area;
                }
            }

            return FacadeAreaFromSurface(surfaceArea, weightedHeights.Sum(entry => entry.PlanArea));
        }

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
