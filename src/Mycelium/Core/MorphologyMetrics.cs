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

        public string ToDisplayString()
        {
            var text = new StringBuilder();
            text.AppendLine("--- Morphology Metrics ---");
            text.AppendLine($"Plan Area Density (λp): {PlanAreaDensity:F3}");
            text.AppendLine($"Open Space Ratio: {OpenSpaceRatio:F3}");
            text.AppendLine($"Park Area Ratio: {ParkAreaRatio:F3}");
            text.AppendLine($"Frontal Area Density (λf, projected, unshielded): {FrontalAreaDensity:F3}");
            text.AppendLine($"Analysis Direction: ({AnalysisDirectionX:F3}, {AnalysisDirectionY:F3})");
            text.AppendLine($"Mean Height: {MeanHeight:F2}");
            text.AppendLine($"Height Std. Dev. (σH): {HeightStandardDeviation:F2}");
            text.AppendLine($"Plan-Area-Weighted Mean Height: {PlanAreaWeightedMeanHeight:F2}");
            text.AppendLine($"Plan-Area-Weighted σH: {PlanAreaWeightedHeightStandardDeviation:F2}");
            text.AppendLine($"Height Min / Median / P90 / Max: {MinimumHeight:F2} / {MedianHeight:F2} / {HeightP90:F2} / {MaximumHeight:F2}");
            text.AppendLine($"Building Plan Area: {BuildingPlanArea:F2}");
            text.AppendLine($"Park Area: {ParkArea:F2}");
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

        public static MorphologyMetricsResult Calculate(Curve boundary, IReadOnlyList<Curve> footprints,
            IReadOnlyList<Brep> masses, IReadOnlyList<Curve> parks, Vector3d analysisDirection)
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
                MassCount = masses?.Count ?? 0
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
