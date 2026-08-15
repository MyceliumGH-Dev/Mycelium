using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Mycelium.Core
{
    /// <summary>
    /// A fully resolved street-network choice: the family plus the sub-option that applies to it.
    /// </summary>
    /// <remarks>
    /// The generator's street network is selectable from the component context menu, which is
    /// convenient interactively but unreachable from a slider or a script. Batch campaigns need to
    /// sweep the sub-options as an ordinary parameter, so the same choice is also expressible as
    /// text and parsed here, outside any Grasshopper type, to keep it testable.
    /// </remarks>
    public struct StreetNetworkSelection
    {
        public StreetNetworkType Family { get; set; }
        public OrthogonalGridType Orthogonal { get; set; }
        public IrregularGridType Irregular { get; set; }
        public DiagonalGridType Diagonal { get; set; }
        public RadialConcentricGridType Radial { get; set; }

        public static StreetNetworkSelection Default => new StreetNetworkSelection
        {
            Family = StreetNetworkType.IrregularGrid,
            Orthogonal = OrthogonalGridType.Regular,
            Irregular = IrregularGridType.RecursiveOrthogonal,
            Diagonal = DiagonalGridType.SingleAxis,
            Radial = RadialConcentricGridType.CivicCore
        };

        /// <summary>
        /// Canonical text for every selectable sub-option, in a stable order. Suitable for
        /// enumerating the design space in a sampling script.
        /// </summary>
        public static IReadOnlyList<string> CanonicalNames { get; } = new[]
        {
            "Irregular/RecursiveOrthogonal",
            "Irregular/DeformedGrid",
            "Irregular/StaggeredGrid",
            "Orthogonal/Regular",
            "Orthogonal/Rectangular",
            "Orthogonal/Cerda",
            "Orthogonal/HierarchicalSuperblock",
            "Diagonal/SingleAxis",
            "Diagonal/CrossAxes",
            "Diagonal/OrthogonalOverlay",
            "RadialConcentric/CivicCore",
            "RadialConcentric/PolygonalRadial",
            "RadialConcentric/FanPlan"
        };

        /// <summary>
        /// Parses a sub-option name. Accepts the canonical "Family/Subtype" form, the display
        /// labels used in the context menu, and the bare sub-option name, all case- accent- and
        /// separator-insensitive.
        /// </summary>
        public static bool TryParse(string text, out StreetNetworkSelection selection)
        {
            selection = Default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string key = Normalize(text);
            if (key.Length == 0)
                return false;

            // "family/subtype" — keep only the sub-option, which already identifies the family.
            int separator = key.IndexOf("__", StringComparison.Ordinal);
            if (separator >= 0 && separator + 2 < key.Length)
                key = key.Substring(separator + 2);

            if (!Lookup.TryGetValue(key, out var resolved))
                return false;

            selection = resolved;
            return true;
        }

        /// <summary>Canonical "Family/Subtype" text for this selection.</summary>
        public string ToCanonicalName()
        {
            switch (Family)
            {
                case StreetNetworkType.OrthogonalGrid:
                    return "Orthogonal/" + Orthogonal;
                case StreetNetworkType.DiagonalGrid:
                    return "Diagonal/" + Diagonal;
                case StreetNetworkType.RadialConcentricGrid:
                    return "RadialConcentric/" + Radial;
                case StreetNetworkType.IrregularGrid:
                default:
                    return "Irregular/" + Irregular;
            }
        }

        private static readonly Dictionary<string, StreetNetworkSelection> Lookup = BuildLookup();

        private static Dictionary<string, StreetNetworkSelection> BuildLookup()
        {
            var lookup = new Dictionary<string, StreetNetworkSelection>(StringComparer.Ordinal);

            void Add(StreetNetworkSelection selection, params string[] aliases)
            {
                foreach (var alias in aliases)
                {
                    string key = Normalize(alias);
                    if (key.Length > 0 && !lookup.ContainsKey(key))
                        lookup[key] = selection;
                }
            }

            foreach (IrregularGridType type in Enum.GetValues(typeof(IrregularGridType)))
            {
                var selection = Default;
                selection.Family = StreetNetworkType.IrregularGrid;
                selection.Irregular = type;
                Add(selection, type.ToString(), type + "Grid");
            }

            foreach (OrthogonalGridType type in Enum.GetValues(typeof(OrthogonalGridType)))
            {
                var selection = Default;
                selection.Family = StreetNetworkType.OrthogonalGrid;
                selection.Orthogonal = type;
                Add(selection, type.ToString(), type + "Grid");
            }

            foreach (DiagonalGridType type in Enum.GetValues(typeof(DiagonalGridType)))
            {
                var selection = Default;
                selection.Family = StreetNetworkType.DiagonalGrid;
                selection.Diagonal = type;
                Add(selection, type.ToString(), type + "Grid");
            }

            foreach (RadialConcentricGridType type in Enum.GetValues(typeof(RadialConcentricGridType)))
            {
                var selection = Default;
                selection.Family = StreetNetworkType.RadialConcentricGrid;
                selection.Radial = type;
                Add(selection, type.ToString(), type + "Grid");
            }

            // Bare family names resolve to that family's default sub-option.
            var irregularDefault = Default;
            Add(irregularDefault, "Irregular", "IrregularGrid");

            var orthogonalDefault = Default;
            orthogonalDefault.Family = StreetNetworkType.OrthogonalGrid;
            Add(orthogonalDefault, "Orthogonal", "OrthogonalGrid");

            var diagonalDefault = Default;
            diagonalDefault.Family = StreetNetworkType.DiagonalGrid;
            Add(diagonalDefault, "Diagonal", "DiagonalGrid");

            var radialDefault = Default;
            radialDefault.Family = StreetNetworkType.RadialConcentricGrid;
            Add(radialDefault, "Radial", "RadialConcentric", "RadialConcentricGrid");

            return lookup;
        }

        /// <summary>
        /// Case-, accent- and whitespace-insensitive key. "Cerdà Grid", "cerda grid" and
        /// "CerdaGrid" all collapse to "cerdagrid". Only path separators ("/", ":", "|") survive,
        /// as "__", so a "family/subtype" pair stays splittable while a two-word display label
        /// does not accidentally split.
        /// </summary>
        private static string Normalize(string text)
        {
            if (text == null)
                return string.Empty;

            string decomposed = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);

            foreach (char character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                    continue;

                if (char.IsLetterOrDigit(character))
                    builder.Append(char.ToLowerInvariant(character));
                else if (character == '/' || character == ':' || character == '|' || character == '\\')
                    builder.Append("__");
            }

            // Collapse runs of separators and trim them from both ends.
            var parts = builder.ToString()
                .Split(new[] { "__" }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join("__", parts);
        }
    }
}
