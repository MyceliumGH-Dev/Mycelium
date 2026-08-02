using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rhino;

namespace Mycelium.Core
{
    /// <summary>
    /// Stable, machine-readable record for regenerating and indexing a Mycelium case.
    /// Geometry stays in external files; this manifest stores parameters, provenance,
    /// counts, and derived metrics.
    /// </summary>
    public sealed class CaseManifest
    {
        public const string CurrentSchemaVersion = "1.0.0";

        public string Schema { get; set; } = "https://github.com/MyceliumGH-Dev/Mycelium/blob/dev/docs/case-manifest.schema.json";
        public string SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string CaseId { get; set; }
        public GeneratorProvenance Generator { get; set; }
        public GenerationParameters Parameters { get; set; }
        public GeometrySummary Geometry { get; set; }
        public DevelopmentMetrics Development { get; set; }
        public MorphologyMetricsResult Morphology { get; set; }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, JsonOptions);
        }

        public string CalculateCaseId()
        {
            var identity = JsonSerializer.Serialize(new
            {
                schemaVersion = SchemaVersion,
                generatorVersion = Generator?.Version,
                parameters = Parameters
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            using (var sha256 = SHA256.Create())
            {
                var digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(identity));
                var text = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest)
                    text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }

        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string InstalledVersion()
        {
            return typeof(CaseManifest).Assembly.GetName().Version?.ToString() ?? "unknown";
        }

        public static string ActiveModelUnits()
        {
            return RhinoDoc.ActiveDoc?.ModelUnitSystem.ToString() ?? "Unknown";
        }
    }

    public sealed class GeneratorProvenance
    {
        public string Name { get; set; } = "Mycelium";
        public string Version { get; set; }
        public string Repository { get; set; } = "https://github.com/MyceliumGH-Dev/Mycelium";
    }

    public sealed class GenerationParameters
    {
        public int Seed { get; set; }
        public string ModelUnits { get; set; }
        public double FloorHeight { get; set; }
        public int Divisions { get; set; }
        public double StreetWidth { get; set; }
        public int RequestedParks { get; set; }
        public bool GenerateFloorSlabs { get; set; }
        public string StreetNetworkFamily { get; set; }
        public string StreetNetworkSubtype { get; set; }
        public IReadOnlyList<string> BuildingConfigurations { get; set; }
        public string TreeConfiguration { get; set; }
        public bool TreeConfigurationProvided { get; set; }
        public Direction2D AnalysisDirection { get; set; }
    }

    public sealed class Direction2D
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class GeometrySummary
    {
        public int Footprints { get; set; }
        public int Masses { get; set; }
        public int Streets { get; set; }
        public int FloorSlabs { get; set; }
        public int Parks { get; set; }
        public int Courtyards { get; set; }
        public int Trees { get; set; }
        public int Parcels { get; set; }
    }

    public sealed class DevelopmentMetrics
    {
        public double SiteArea { get; set; }
        public double GrossFloorArea { get; set; }
        public double GrossInternalArea { get; set; }
        public double NetInternalArea { get; set; }
        public double FloorAreaRatio { get; set; }
        public int EstimatedUnits { get; set; }
    }
}
