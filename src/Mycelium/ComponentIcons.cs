using System.Collections.Concurrent;
using System.Drawing;

namespace Mycelium
{
    /// <summary>
    /// Loads component icons embedded as PNG resources under Icons/.
    /// </summary>
    internal static class ComponentIcons
    {
        private static readonly ConcurrentDictionary<string, Bitmap> Cache = new ConcurrentDictionary<string, Bitmap>();

        /// <summary>
        /// Returns the 24x24 icon with the given base file name (without extension), or null if missing.
        /// </summary>
        internal static Bitmap Get(string name)
        {
            return Cache.GetOrAdd(name, key =>
            {
                // The stream must stay open for the lifetime of the Bitmap; icons are cached forever.
                var stream = typeof(ComponentIcons).Assembly
                    .GetManifestResourceStream($"Mycelium.Icons.{key}.png");
                return stream == null ? null : new Bitmap(stream);
            });
        }
    }
}
