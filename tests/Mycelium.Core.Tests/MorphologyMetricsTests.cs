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
