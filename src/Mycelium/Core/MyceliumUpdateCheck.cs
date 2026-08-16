using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mycelium.Core
{
    /// <summary>
    /// Checks the Yak package registry for a newer published Mycelium version. Pure HTTP + version
    /// compare, no Rhino dependency. The result is cached per session and throttled to at most one
    /// network hit per day (persisted under %AppData%/Mycelium/update-check.json), and a
    /// version-specific "skip" flag is honoured. Every failure mode (offline, timeout, malformed
    /// JSON, unparseable version) is swallowed and reported as "no update", so a version check can
    /// never block Grasshopper load or throw into the UI.
    ///
    /// Offers follow the channel the user is already on: someone running a stable is only ever shown
    /// stables, while someone running a beta (<c>X.Y.Z-beta.W</c>) is shown whatever is newest —
    /// the next beta or the stable that supersedes it. The registry lists both kinds in one array
    /// and its newest entry is frequently a pre-release, so taking the array's first element would
    /// prompt every stable user to "update" to a beta.
    ///
    /// Adopted from Eddy3D's MetaFOAM.Eddy3DUpdateCheck, including the channel rule it learned the
    /// hard way in the field.
    /// </summary>
    public static class MyceliumUpdateCheck
    {
        public const string PackageRootEnvironmentVariable = "MYCELIUM_YAK_PACKAGE_ROOT";

        public sealed class UpdateInfo
        {
            public bool Available { get; set; }
            public string Installed { get; set; }
            public string InstalledDate { get; set; }
            public string Latest { get; set; }
            public string LatestDate { get; set; }
        }

        // The versions array endpoint carries publish dates for both the installed and latest build.
        private const string YakPackageUrl = "https://yak.rhino3d.com/versions/Mycelium";
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        private static readonly object Gate = new object();
        private static UpdateInfo _session;
        private static string _sessionInstalledVersion;

        private static string StateDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mycelium");
        private static string StateFile => Path.Combine(StateDir, "update-check.json");

        /// <summary>
        /// Update info for the given installed version. Computed once, then cached for the session.
        /// <paramref name="force"/> (a user-initiated "check now") bypasses both caches: it skips the
        /// session memo and the 24h network throttle, always hitting Yak for a fresh answer, and the
        /// result replaces the cached one for the rest of the session.
        /// </summary>
        public static async Task<UpdateInfo> CheckAsync(string installedVersion, bool force = false)
        {
            var effectiveInstalledVersion = GetEffectiveInstalledVersion(installedVersion);

            if (!force)
            {
                lock (Gate)
                {
                    if (_session != null && string.Equals(_sessionInstalledVersion, effectiveInstalledVersion, StringComparison.OrdinalIgnoreCase))
                        return _session;
                }
            }

            var resolved = await ResolveVersionsAsync(effectiveInstalledVersion, force).ConfigureAwait(false);
            var info = new UpdateInfo
            {
                // The channel guard is applied here too, not only when picking `latest` out of the
                // registry: a state file written by an older build can still hold a beta as "latest",
                // and the 24h throttle would serve it from cache for a day after upgrading.
                Available = IsOfferable(effectiveInstalledVersion, resolved.Latest)
                            && IsNewer(effectiveInstalledVersion, resolved.Latest),
                Installed = effectiveInstalledVersion,
                InstalledDate = resolved.InstalledDate,
                Latest = resolved.Latest,
                LatestDate = resolved.LatestDate
            };

            lock (Gate)
            {
                if (force || _session == null || !string.Equals(_sessionInstalledVersion, effectiveInstalledVersion, StringComparison.OrdinalIgnoreCase))
                {
                    _session = info;
                    _sessionInstalledVersion = effectiveInstalledVersion;
                }

                return _session;
            }
        }

        /// <summary>
        /// True if <paramref name="latestVersion"/> is a strictly higher release than
        /// <paramref name="installedVersion"/>. Both are cleaned of any "-beta" suffix first; either
        /// being unparseable yields false (never prompt on garbage). This is a PURE version
        /// comparison — filtering pre-releases out of the offer is the caller's job
        /// (see <see cref="IsPreRelease"/>).
        /// </summary>
        public static bool IsNewer(string installedVersion, string latestVersion) =>
            Version.TryParse(Clean(installedVersion), out var iv)
            && Version.TryParse(Clean(latestVersion), out var lv)
            && iv < lv;

        /// <summary>
        /// True for a pre-release (beta) package: the SemVer hyphen suffix Mycelium betas carry
        /// (<c>0.2.0-beta.4</c>). Stable releases are plain <c>MAJOR.MINOR.PATCH.BUILD</c>.
        /// </summary>
        public static bool IsPreRelease(string version) =>
            !string.IsNullOrWhiteSpace(version) && version.Contains("-");

        /// <summary>
        /// True when <paramref name="candidateVersion"/> belongs to the channel someone running
        /// <paramref name="installedVersion"/> should be offered. Stable users get stables only;
        /// beta users already opted into pre-releases, so they get those as well as stables.
        /// (Says nothing about which is NEWER — that is <see cref="IsNewer"/>.)
        /// </summary>
        public static bool IsOfferable(string installedVersion, string candidateVersion) =>
            !IsPreRelease(candidateVersion) || IsPreRelease(installedVersion);

        /// <summary>
        /// The highest version in <paramref name="versions"/> that may be offered to someone running
        /// <paramref name="installedVersion"/> (see <see cref="IsOfferable"/>), or null when the list
        /// holds none. The winner is chosen by comparison rather than by the registry's array order,
        /// so the answer never depends on how Yak happens to sort.
        /// </summary>
        public static string SelectLatestFor(string installedVersion, IEnumerable<string> versions)
        {
            string best = null;
            foreach (var version in versions ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(version) || !IsOfferable(installedVersion, version)) continue;
                if (best == null || IsHigherThan(version, best)) best = version;
            }

            return best;
        }

        /// <summary>
        /// The version the user effectively runs: the loaded assembly's, unless a locally installed
        /// Yak package carries a higher one (which happens right after an update, before Rhino is
        /// restarted — otherwise the user is nagged about a version they already have).
        /// </summary>
        public static string GetEffectiveInstalledVersion(string loadedAssemblyVersion)
        {
            var best = loadedAssemblyVersion;
            foreach (var packageVersion in EnumerateLocalPackageVersions())
                if (IsHigherThan(packageVersion, best))
                    best = packageVersion;
            return best;
        }

        /// <summary>True if the user chose "skip this version" for <paramref name="version"/>.</summary>
        public static bool IsSkipped(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            return string.Equals(ReadState().Skipped, version, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Records that the user does not want to be reminded about <paramref name="version"/> again.</summary>
        public static void Skip(string version)
        {
            var s = ReadState();
            WriteState(s.LastCheckUtc, s.Installed, s.Latest, s.LatestDate, s.InstalledDate, version, s.Never);
        }

        /// <summary>True if the user permanently opted out of update notifications ("Never remind me again").</summary>
        public static bool IsNeverRemind() => ReadState().Never;

        /// <summary>Permanently disables update notifications. Re-enable by deleting the state file (see <see cref="StateFile"/>).</summary>
        public static void SetNeverRemind()
        {
            var s = ReadState();
            WriteState(s.LastCheckUtc, s.Installed, s.Latest, s.LatestDate, s.InstalledDate, s.Skipped, never: true);
        }

        /// <summary>The state file's path, so the UI can tell the user where the opt-outs live.</summary>
        public static string StateFilePath => StateFile;

        private static async Task<(string Latest, string LatestDate, string InstalledDate)> ResolveVersionsAsync(string installed, bool force = false)
        {
            try
            {
                var s = ReadState();
                // Throttle: reuse today's answer instead of hitting Yak on every Grasshopper launch.
                // A forced (user-initiated) check skips it and always asks Yak.
                if (!force && s.LastCheckUtc.HasValue && (DateTime.UtcNow - s.LastCheckUtc.Value) < TimeSpan.FromHours(24)
                    && string.Equals(s.Installed, installed, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(s.Latest))
                    return (s.Latest, s.LatestDate, s.InstalledDate);

                var json = await Http.GetStringAsync(YakPackageUrl).ConfigureAwait(false);
                using (var doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                    {
                        var published = new List<(string Version, string Date)>();

                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            if (!el.TryGetProperty("version", out var v)) continue;
                            var ver = v.GetString();
                            if (string.IsNullOrEmpty(ver)) continue;

                            string dateStr = null;
                            if (el.TryGetProperty("created_at", out var d))
                            {
                                var ds = d.GetString();
                                if (!string.IsNullOrEmpty(ds)
                                    && DateTime.TryParse(ds, CultureInfo.InvariantCulture,
                                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                                    dateStr = dt.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
                            }

                            published.Add((ver, dateStr));
                        }

                        // Betas live in this same array and routinely lead it (the registry is
                        // SemVer-sorted, so 0.2.0-beta.4 outranks the 0.1.0.4 stable) — hence the
                        // channel filter.
                        var latest = SelectLatestFor(installed, published.Select(p => p.Version));
                        var latestDate = published.FirstOrDefault(p =>
                            string.Equals(p.Version, latest, StringComparison.OrdinalIgnoreCase)).Date;
                        var instDate = published.FirstOrDefault(p =>
                            string.Equals(p.Version, installed, StringComparison.OrdinalIgnoreCase)).Date;

                        if (latest != null)
                        {
                            WriteState(DateTime.UtcNow, installed, latest, latestDate, instDate, s.Skipped, s.Never);
                            return (latest, latestDate, instDate);
                        }
                    }
                }
            }
            catch
            {
                // offline / timeout / parse — fall through to the last known value.
            }

            var state = ReadState();
            return string.Equals(state.Installed, installed, StringComparison.OrdinalIgnoreCase)
                ? (state.Latest ?? "", state.LatestDate ?? "", state.InstalledDate ?? "")
                : ("", "", "");
        }

        // System.Version can't parse a "-beta.N" suffix; drop it so a stable installed/latest still compares.
        private static string Clean(string v) =>
            string.IsNullOrWhiteSpace(v) ? v : (v.Contains("-") ? v.Split('-')[0] : v);

        private static bool IsHigherThan(string candidateVersion, string currentVersion)
        {
            var candidateOk = Version.TryParse(Clean(candidateVersion), out var candidate);
            var currentOk = Version.TryParse(Clean(currentVersion), out var current);
            if (!candidateOk) return false;
            if (!currentOk) return true;
            return candidate > current;
        }

        private static IEnumerable<string> EnumerateLocalPackageVersions()
        {
            foreach (var packageRoot in EnumeratePackageRoots())
            {
                foreach (var myceliumDir in EnumerateMyceliumPackageDirs(packageRoot))
                {
                    foreach (var versionDir in SafeEnumerateDirectories(myceliumDir))
                    {
                        var version = Path.GetFileName(versionDir);
                        if (Version.TryParse(Clean(version), out _))
                            yield return version;
                    }
                }
            }
        }

        private static IEnumerable<string> EnumeratePackageRoots()
        {
            var overrideRoot = Environment.GetEnvironmentVariable(PackageRootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                yield return overrideRoot.Trim();
                yield break;
            }

            // RuntimeInformation, not OperatingSystem.IsWindows(): this file is also compiled into
            // the net48 test project, where the OperatingSystem helpers don't exist.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "McNeel", "Rhinoceros", "packages");
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "McNeel", "Rhinoceros", "packages");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "McNeel", "Rhinoceros", "packages");
            }
            else
            {
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "McNeel", "Rhinoceros", "packages");
            }
        }

        private static IEnumerable<string> EnumerateMyceliumPackageDirs(string packageRoot)
        {
            if (string.IsNullOrWhiteSpace(packageRoot)) yield break;

            if (string.Equals(Path.GetFileName(packageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    "Mycelium", StringComparison.OrdinalIgnoreCase))
            {
                yield return packageRoot;
                yield break;
            }

            // Packages are nested one Rhino-version folder deep: packages/8.0/Mycelium/<version>/
            foreach (var rhinoVersionDir in SafeEnumerateDirectories(packageRoot))
            {
                var packageDir = Path.Combine(rhinoVersionDir, "Mycelium");
                if (Directory.Exists(packageDir))
                    yield return packageDir;
            }
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string path)
        {
            try
            {
                return Directory.Exists(path)
                    ? Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly).ToArray()
                    : Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        // --- tiny persisted state: { lastCheckUtc, installed, latest, latestDate, installedDate, skipped, never } ---
        private struct State
        {
            public DateTime? LastCheckUtc;
            public string Installed;
            public string Latest;
            public string LatestDate;
            public string InstalledDate;
            public string Skipped;
            public bool Never;
        }

        private static State ReadState()
        {
            try
            {
                if (!File.Exists(StateFile)) return default(State);
                using (var doc = JsonDocument.Parse(File.ReadAllText(StateFile)))
                {
                    var r = doc.RootElement;
                    DateTime? last = null;
                    if (r.TryGetProperty("lastCheckUtc", out var l) && l.ValueKind == JsonValueKind.String
                        && DateTime.TryParse(l.GetString(), CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                        last = dt;

                    return new State
                    {
                        LastCheckUtc = last,
                        Installed = r.TryGetProperty("installed", out var iv) ? iv.GetString() : null,
                        Latest = r.TryGetProperty("latest", out var la) ? la.GetString() : null,
                        LatestDate = r.TryGetProperty("latestDate", out var ld) ? ld.GetString() : null,
                        InstalledDate = r.TryGetProperty("installedDate", out var id) ? id.GetString() : null,
                        Skipped = r.TryGetProperty("skipped", out var sk) ? sk.GetString() : null,
                        Never = r.TryGetProperty("never", out var nv) && nv.ValueKind == JsonValueKind.True
                    };
                }
            }
            catch
            {
                return default(State);
            }
        }

        private static void WriteState(DateTime? lastCheckUtc, string installed, string latest, string latestDate, string installedDate, string skipped, bool never)
        {
            try
            {
                Directory.CreateDirectory(StateDir);
                var payload = JsonSerializer.Serialize(new
                {
                    lastCheckUtc = lastCheckUtc?.ToString("o", CultureInfo.InvariantCulture),
                    installed,
                    latest,
                    latestDate,
                    installedDate,
                    skipped,
                    never
                });
                File.WriteAllText(StateFile, payload);
            }
            catch
            {
                // best-effort cache; a failed write just means we re-check next launch.
            }
        }
    }
}
