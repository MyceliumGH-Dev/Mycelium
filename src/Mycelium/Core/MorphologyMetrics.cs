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
        public double MinimumHeight { get; set; }
        public double MaximumHeight { get; set; }
        public double MedianHeight { get; set; }
        public double HeightP90 { get; set; }
        public double AnalysisDirectionX { get; set; }
        public double AnalysisDirectionY { get; set; }
        public int BuildingFootprintCount { get; set; }

        public string ToDisplayString()
        {
            var text = new StringBuilder();
            text.AppendLine("--- Morphology Metrics ---");
            text.AppendLine($"Plan Area Density (λp): {PlanAreaDensity:F3}");
            text.AppendLine($"Open Space Ratio: {OpenSpaceRatio:F3}");
            text.AppendLine($"Park Area Ratio: {ParkAreaRatio:F3}");
            text.AppendLine($"Frontal Area Density (λf): {FrontalAreaDensity:F3}");
            text.AppendLine($"Analysis Direction: ({AnalysisDirectionX:F3}, {AnalysisDirectionY:F3})");
            text.AppendLine($"Mean Height: {MeanHeight:F2}");
            text.AppendLine($"Height Std. Dev. (σH): {HeightStandardDeviation:F2}");
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
            double planArea = CalculatePlanArea(footprints);
            double parkArea = SumCurveAreas(parks);

            var direction = new Vector3d(analysisDirection.X, analysisDirection.Y, 0.0);
            if (!direction.Unitize())
                direction = Vector3d.XAxis;

            var validHeights = masses == null
                ? new List<double>()
                : masses.Where(mass => mass != null)
                    .Select(mass =>
                    {
                        var box = mass.GetBoundingBox(true);
                        return box.IsValid ? Math.Max(0.0, box.Max.Z - box.Min.Z) : 0.0;
                    })
                    .Where(h => !double.IsNaN(h) && !double.IsInfinity(h) && h >= 0.0)
                    .OrderBy(h => h).ToList();

            var heightStatistics = CalculateHeightStatistics(validHeights);
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
                MinimumHeight = heightStatistics.Minimum,
                MaximumHeight = heightStatistics.Maximum,
                MedianHeight = heightStatistics.Median,
                HeightP90 = heightStatistics.P90,
                AnalysisDirectionX = direction.X,
                AnalysisDirectionY = direction.Y,
                BuildingFootprintCount = masses?.Count ?? 0
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

        private static double CalculatePlanArea(IReadOnlyList<Curve> footprints)
        {
            if (footprints == null || footprints.Count == 0)
                return 0.0;

            var planarRegions = Brep.CreatePlanarBreps(footprints, 0.001);
            if (planarRegions == null || planarRegions.Length == 0)
                return SumCurveAreas(footprints);

            double sum = 0.0;
            foreach (var region in planarRegions)
            {
                using (var properties = AreaMassProperties.Compute(region))
                {
                    if (properties != null)
                        sum += properties.Area;
                }
            }
            return sum;
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
