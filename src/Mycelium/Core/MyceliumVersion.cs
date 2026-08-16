using System.Reflection;

namespace Mycelium.Core
{
    /// <summary>
    /// The plugin's own version, as stamped from manifest.yml at build time (4-part X.Y.Z.W).
    /// Single source for the pieces that key off it: the Mycelium-Templates branch to sync from
    /// and the "installed" side of the Yak update check.
    /// </summary>
    internal static class MyceliumVersion
    {
        /// <summary>The running assembly's version, or an empty string when it can't be read.</summary>
        internal static string Current
        {
            get
            {
                var version = typeof(MyceliumVersion).Assembly.GetName().Version;
                return version == null ? string.Empty : version.ToString();
            }
        }
    }
}
