using System.Text.Json;
using Mycelium.Core;
using Xunit;

namespace Mycelium.Core.Tests
{
    public class MorphologyMetricsTests
    {
        [Fact]
        public void HeightStatistics_ReturnExpectedPopulationValues()
        {
            var result = MorphologyMetrics.CalculateHeightStatistics(new[] { 10.0, 20.0 });

            Assert.Equal(15.0, result.Mean, 8);
            Assert.Equal(5.0, result.StandardDeviation, 8);
            Assert.Equal(10.0, result.Minimum, 8);
            Assert.Equal(20.0, result.Maximum, 8);
            Assert.Equal(15.0, result.Median, 8);
            Assert.Equal(19.0, result.P90, 8);
        }

        [Fact]
        public void WeightedHeightStatistics_FollowPlanArea()
        {
            // One large low block and one small tower. Unweighted statistics treat them equally;
            // plan-area weighting must pull the mean toward the block that covers more ground.
            var entries = new[]
            {
                new MorphologyMetrics.WeightedHeight(10.0, 900.0),
                new MorphologyMetrics.WeightedHeight(50.0, 100.0)
            };

            var weighted = MorphologyMetrics.CalculateWeightedHeightStatistics(entries);
            var unweighted = MorphologyMetrics.CalculateHeightStatistics(new[] { 10.0, 50.0 });

            Assert.Equal(14.0, weighted.Mean, 8);
            Assert.Equal(30.0, unweighted.Mean, 8);
            Assert.Equal(12.0, weighted.StandardDeviation, 8);
            Assert.True(weighted.StandardDeviation < unweighted.StandardDeviation);
        }

        [Fact]
        public void WeightedHeightStatistics_FallBackWhenNoPlanAreaIsAvailable()
        {
            var entries = new[]
            {
                new MorphologyMetrics.WeightedHeight(10.0, 0.0),
                new MorphologyMetrics.WeightedHeight(20.0, 0.0)
            };

            Assert.Equal(15.0, MorphologyMetrics.CalculateWeightedHeightStatistics(entries).Mean, 8);
        }

        [Fact]
        public void CaseManifest_UsesStableCamelCaseSchema()
        {
            var manifest = new CaseManifest
            {
                Generator = new GeneratorProvenance { Version = "0.1.0.4" },
                Parameters = new GenerationParameters
                {
                    Seed = 42,
                    ModelUnits = "Meters",
                    BuildingConfigurations = new[] { "0|3|6" },
                    AnalysisDirection = new Direction2D { X = 1, Y = 0 }
                },
                Geometry = new GeometrySummary { Masses = 2 },
                Development = new DevelopmentMetrics { SiteArea = 10000 },
                Morphology = new MorphologyMetricsResult { PlanAreaDensity = 0.04 }
            };
            manifest.CaseId = manifest.CalculateCaseId();

            using (var json = JsonDocument.Parse(manifest.ToJson()))
            {
                var root = json.RootElement;
                Assert.Equal(CaseManifest.CurrentSchemaVersion, root.GetProperty("schemaVersion").GetString());
                Assert.Matches("^[0-9a-f]{64}$", root.GetProperty("caseId").GetString());
                Assert.Equal(42, root.GetProperty("parameters").GetProperty("seed").GetInt32());
                Assert.Equal(2, root.GetProperty("geometry").GetProperty("masses").GetInt32());
                Assert.Equal(0.04, root.GetProperty("morphology").GetProperty("planAreaDensity").GetDouble(), 8);
            }
        }
    }
}
