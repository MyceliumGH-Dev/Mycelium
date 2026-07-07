using System;
using System.Globalization;

namespace Mycelium.Core
{
    /// <summary>
    /// Tree generation parameters, produced by the tree config component and
    /// consumed by the massing generator.
    /// Serialized as a culture-invariant pipe-delimited string:
    /// DensityPercent|MinDiameter|MaxDiameter|GenerateInCourtyards
    /// </summary>
    public struct TreeConfig
    {
        /// <summary>Tree density in percent; 100% places roughly one tree per 25 m².</summary>
        public double DensityPercent;
        public double MinDiameter;
        public double MaxDiameter;
        public bool GenerateInCourtyards;

        public static TreeConfig Default => new TreeConfig
        {
            DensityPercent = 10.0,
            MinDiameter = 2.0,
            MaxDiameter = 5.0,
            GenerateInCourtyards = true,
        };

        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            return string.Join("|",
                DensityPercent.ToString("F2", inv),
                MinDiameter.ToString("F2", inv),
                MaxDiameter.ToString("F2", inv),
                GenerateInCourtyards.ToString(inv));
        }

        public static bool TryParse(string text, out TreeConfig config)
        {
            config = Default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var parts = text.Split('|');
            if (parts.Length != 4)
                return false;

            var inv = CultureInfo.InvariantCulture;
            try
            {
                config.DensityPercent = double.Parse(parts[0], NumberStyles.Float, inv);
                config.MinDiameter = double.Parse(parts[1], NumberStyles.Float, inv);
                config.MaxDiameter = double.Parse(parts[2], NumberStyles.Float, inv);
                config.GenerateInCourtyards = bool.Parse(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            return true;
        }
    }
}
