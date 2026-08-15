using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        public const string CurrentSchemaVersion = "1.1.0";

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

        /// <summary>
        /// Deterministic case identifier over the full regeneration input: schema version,
        /// generator version, and every generation parameter including the canonical boundary
        /// digest.
        /// </summary>
        /// <remarks>
        /// The identity string is produced by <see cref="CanonicalIdentity"/> rather than by a
        /// plain serializer call, because a hash that is archived alongside a dataset must not
        /// depend on reflection property order or on the order in which building configurations
        /// happened to be wired.
        /// </remarks>
        public string CalculateCaseId()
        {
            using (var sha256 = SHA256.Create())
            {
                var digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(CanonicalIdentity()));
                var text = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest)
                    text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        /// <summary>
        /// Canonical identity string hashed by <see cref="CalculateCaseId"/>. Exposed so a
        /// pipeline can archive or diff the exact preimage of a case identifier.
        /// </summary>
        public string CanonicalIdentity()
        {
            var parameters = Parameters;
            var normalizedConfigurations = parameters?.BuildingConfigurations == null
                ? null
                : parameters.BuildingConfigurations
                    .Where(configuration => configuration != null)
                    .OrderBy(configuration => configuration, StringComparer.Ordinal)
                    .ToArray();

            var identity = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = SchemaVersion ?? string.Empty,
                ["generatorVersion"] = Generator?.Version ?? string.Empty,
                ["boundaryDigest"] = parameters?.BoundaryDigest ?? string.Empty,
                ["boundaryCanonicalTolerance"] = Number(parameters?.BoundaryCanonicalTolerance),
                ["boundaryCanonicalDecimals"] = parameters?.BoundaryCanonicalDecimals.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["seed"] = parameters?.Seed.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["modelUnits"] = parameters?.ModelUnits ?? string.Empty,
                ["modelAbsoluteTolerance"] = Number(parameters?.ModelAbsoluteTolerance),
                ["floorHeight"] = Number(parameters?.FloorHeight),
                ["divisions"] = parameters?.Divisions.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["streetWidth"] = Number(parameters?.StreetWidth),
                ["requestedParks"] = parameters?.RequestedParks.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["generateFloorSlabs"] = parameters?.GenerateFloorSlabs.ToString() ?? string.Empty,
                ["streetNetworkFamily"] = parameters?.StreetNetworkFamily ?? string.Empty,
                ["streetNetworkSubtype"] = parameters?.StreetNetworkSubtype ?? string.Empty,
                ["buildingConfigurations"] = normalizedConfigurations == null
                    ? string.Empty
                    : string.Join("\u001f", normalizedConfigurations),
                ["treeConfiguration"] = parameters?.TreeConfiguration ?? string.Empty,
                ["treeConfigurationProvided"] = parameters?.TreeConfigurationProvided.ToString() ?? string.Empty,
                ["analysisDirectionX"] = Number(parameters?.AnalysisDirection?.X),
                ["analysisDirectionY"] = Number(parameters?.AnalysisDirection?.Y)
            };

            var text = new StringBuilder();
            foreach (var entry in identity)
                text.Append(entry.Key).Append('=').Append(entry.Value).Append('\u001e');
            return text.ToString();
        }

        /// <summary>Round-trippable, culture-invariant number text for the identity string.</summary>
        private static string Number(double? value)
        {
            if (!value.HasValue)
                return string.Empty;
            double magnitude = value.Value;
            if (double.IsNaN(magnitude) || double.IsInfinity(magnitude))
                return string.Empty;
            if (magnitude == 0.0)
                magnitude = 0.0;
            return magnitude.ToString("R", CultureInfo.InvariantCulture);
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

        public static double ActiveModelAbsoluteTolerance()
        {
            return RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
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

        /// <summary>
        /// Document absolute tolerance in effect when the case was generated. Recorded because
        /// tolerance-sensitive operations can change the output for otherwise identical inputs,
        /// so it belongs to the regeneration record rather than to the ambient environment.
        /// </summary>
        public double ModelAbsoluteTolerance { get; set; }

        /// <summary>
        /// SHA-256 of the canonical site-boundary form. The boundary is part of the case identity;
        /// without it, two unrelated sites sharing a parameter vector would collide.
        /// </summary>
        public string BoundaryDigest { get; set; }

        public double BoundaryCanonicalTolerance { get; set; }
        public int BoundaryCanonicalDecimals { get; set; }
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

        /// <summary>
        /// Number of guarded boolean fallbacks taken while generating this case. A non-zero count
        /// means at least one footprint was kept untrimmed after a failed intersection and may
        /// violate its setback, so the case should be inspected before entering a dataset.
        /// </summary>
        public int BooleanFallbacks { get; set; }
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
