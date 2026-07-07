using System;
using System.Globalization;

namespace Mycelium.Core
{
    /// <summary>
    /// Building typologies supported by the massing generator.
    /// Enum values are part of the config wire format; do not renumber.
    /// </summary>
    public enum BuildingType
    {
        Courtyard = 0,
        Linear = 1,
        Point = 2,
        LShape = 3,
        UShape = 4,
        Tower = 5,
    }

    /// <summary>
    /// Parameters for one allowed building typology, produced by the config components
    /// and consumed by the massing generator.
    /// Serialized as a culture-invariant pipe-delimited string:
    /// Type|MinFloors|MaxFloors|CornerRadius|MinArea|MinSetback|MaxSetback|MinDepth|MaxDepth
    /// </summary>
    public struct BuildingTypeConfig
    {
        public BuildingType Type;
        public double MinFloors;
        public double MaxFloors;
        public double CornerRadius;
        public double MinArea;
        public double MinSetback;
        public double MaxSetback;
        public double MinDepth;
        public double MaxDepth;

        /// <summary>Default configuration: courtyard block, 3-6 floors.</summary>
        public static BuildingTypeConfig Default => new BuildingTypeConfig
        {
            Type = BuildingType.Courtyard,
            MinFloors = 3.0,
            MaxFloors = 6.0,
            CornerRadius = 0.0,
            MinArea = 100.0,
            MinSetback = 3.0,
            MaxSetback = 3.0,
            MinDepth = 12.0,
            MaxDepth = 12.0,
        };

        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            return string.Join("|",
                ((int)Type).ToString(inv),
                MinFloors.ToString(inv),
                MaxFloors.ToString(inv),
                CornerRadius.ToString(inv),
                MinArea.ToString(inv),
                MinSetback.ToString(inv),
                MaxSetback.ToString(inv),
                MinDepth.ToString(inv),
                MaxDepth.ToString(inv));
        }

        /// <summary>
        /// Parses a config string. Accepts shorter legacy strings (missing trailing fields
        /// fall back to defaults). Returns false if the string is malformed.
        /// </summary>
        public static bool TryParse(string text, out BuildingTypeConfig config)
        {
            config = Default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var parts = text.Split('|');
            if (parts.Length < 3)
                return false;

            var inv = CultureInfo.InvariantCulture;
            try
            {
                config.Type = (BuildingType)int.Parse(parts[0], inv);
                config.MinFloors = double.Parse(parts[1], NumberStyles.Float, inv);
                config.MaxFloors = double.Parse(parts[2], NumberStyles.Float, inv);

                if (parts.Length >= 4)
                    config.CornerRadius = double.Parse(parts[3], NumberStyles.Float, inv);
                if (parts.Length >= 5)
                    config.MinArea = double.Parse(parts[4], NumberStyles.Float, inv);
                if (parts.Length >= 7)
                {
                    config.MinSetback = double.Parse(parts[5], NumberStyles.Float, inv);
                    config.MaxSetback = double.Parse(parts[6], NumberStyles.Float, inv);
                }
                if (parts.Length >= 9)
                {
                    config.MinDepth = double.Parse(parts[7], NumberStyles.Float, inv);
                    config.MaxDepth = double.Parse(parts[8], NumberStyles.Float, inv);
                }
            }
            catch (FormatException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }

            return true;
        }
    }
}
