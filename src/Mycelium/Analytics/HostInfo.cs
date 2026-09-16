using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

#nullable enable

namespace Mycelium.Analytics;

/// <summary>
/// The machine facts every analytics payload carries: operating system, architecture, UI language
/// and screen size. Deliberately self-contained — no Rhino, no Provisioning, no Grasshopper — so
/// the whole <c>Analytics</c> folder stays the portable unit it already is across Eddy3D, Urbano,
/// Mycelium and MetaMAP.
/// </summary>
/// <remarks>
/// <para><b>Why this exists: Umami reads the OS out of the User-Agent, and Mycelium used to send a
/// hardcoded one.</b> Every payload declared
/// <c>Mozilla/5.0 (Windows NT 10.0; Win64; x64) … Chrome/120.0.0.0</c> regardless of the host, so
/// the dashboard reported 100 % Windows 10 / Chrome / desktop — including every Mac. Umami's server
/// parses the header with <c>detect-browser</c>, whose OS table is a list of regexes over that
/// platform token (<c>/mac os x/i</c> → <c>Mac OS</c>, <c>/(Windows NT 10.0)/</c> →
/// <c>Windows 10</c>), and it has no other input; the same is true of the <c>screen</c> field,
/// which is the ONLY thing <c>getDevice</c> uses to decide mobile/tablet/laptop/desktop. A
/// constant in either place is not a missing dimension in the dashboard, it is a wrong one.</para>
///
/// <para><b>The platform token is coarse on purpose.</b> Measured against <c>detect-browser</c>
/// 5.3.0 itself, not from memory: the old header answers <c>Windows 10</c>, a
/// <c>Macintosh; Intel Mac OS X 10_15_7</c> token answers <c>Mac OS</c>, and both
/// <c>X11; Linux x86_64</c> and <c>X11; Linux aarch64</c> answer <c>Linux</c>. The answer is a
/// NAME with no version in it, so there is nothing a richer token could buy — a real
/// <c>Mac OS X 26_5_2</c> was measured to parse identically, to <c>Mac OS</c>. So
/// <see cref="UserAgentPlatform"/> emits what a real browser on that OS emits, down to the
/// <c>10_15_7</c> that Chrome and Safari froze years ago, and the precise version travels as the
/// <c>os_version</c> event property, where it is a value rather than a regex match.</para>
///
/// <para><b>The User-Agent is one of the three inputs to Umami's session id</b>
/// (<c>uuid(websiteId, ip, userAgent, monthlySalt)</c>), so changing it re-keys every machine
/// once. That is a one-off step in the visitor series and is why the plugin VERSION must never
/// enter the header: it would re-key every machine again at every upgrade, counting one machine
/// as two visitors for that month. Version belongs in the payload, never in the UA.</para>
/// </remarks>
public static class HostInfo
{
    private const string FallbackScreen = "1920x1080";

    private static string _screen = FallbackScreen;

    /// <summary>
    /// Screen size as <c>WIDTHxHEIGHT</c>. Umami derives the device class from this and from
    /// nothing else, so a constant here labels every machine a desktop. Defaults to
    /// <see cref="FallbackScreen"/> until <see cref="ProbeScreen"/> has run on the UI thread.
    /// </summary>
    public static string Screen
    {
        get => _screen;
        set => _screen = string.IsNullOrWhiteSpace(value) ? FallbackScreen : value.Trim();
    }

    /// <summary>
    /// Reads the primary screen through Eto and caches it in <see cref="Screen"/>. Must be called
    /// on the UI thread — Eto.Mac answers from AppKit, which has no meaning on a worker — which is
    /// why the plugin's priority load calls it rather than the background sender. Best-effort: a
    /// host without a usable Eto platform keeps the fallback.
    /// </summary>
    /// <remarks>
    /// Eto is reached by REFLECTION, not by a reference, and that is the one thing about this
    /// method worth explaining. This whole folder is copied verbatim into four plugins
    /// (Eddy3D, Urbano, Mycelium, MetaMAP) and they do not agree about Eto: Mycelium references
    /// <c>Eto.Forms</c> outright, Eddy3D and MetaMAP inherit it from the Grasshopper package, and
    /// <c>Urbano.Core</c> is a Rhino-free library that has no window stack at all. A compile-time
    /// reference would make the file un-portable to the one project that most needs the fallback
    /// path anyway, so the type is looked up at runtime and its absence is simply the fallback.
    /// </remarks>
    public static void ProbeScreen()
    {
        try
        {
            var screenType = Type.GetType("Eto.Forms.Screen, Eto")
                             ?? Type.GetType("Eto.Forms.Screen, Eto.Forms");
            if (screenType == null) return;

            object? primary = screenType
                .GetProperty("PrimaryScreen", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            if (primary == null) return;

            object? bounds = primary.GetType().GetProperty("Bounds")?.GetValue(primary);
            if (bounds == null) return;

            var boundsType = bounds.GetType();
            object? width = boundsType.GetProperty("Width")?.GetValue(bounds);
            object? height = boundsType.GetProperty("Height")?.GetValue(bounds);
            if (width == null || height == null) return;

            int w = (int)Math.Round(Convert.ToDouble(width, CultureInfo.InvariantCulture));
            int h = (int)Math.Round(Convert.ToDouble(height, CultureInfo.InvariantCulture));

            if (w > 0 && h > 0)
            {
                Screen = w.ToString(CultureInfo.InvariantCulture) + "x" +
                         h.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            // No Eto platform (headless, a test host, a Rhino-free core): keep the fallback.
        }
    }

    /// <summary>Operating system family as a low-cardinality property value: Windows, macOS, Linux.</summary>
    public static string OsFamily()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macOS";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "Linux";
        }
        catch
        {
            // Fall through.
        }

        return "unknown";
    }

    /// <summary>
    /// Product version of the operating system — <c>10.0.26100</c>, <c>26.5.2</c>. On macOS
    /// <see cref="Environment.OSVersion"/> carries the PRODUCT version, not the Darwin kernel
    /// version that <see cref="RuntimeInformation.OSDescription"/> reports; the revision segment
    /// is dropped because it is meaningless on every platform here and would multiply the
    /// property's cardinality for nothing.
    /// </summary>
    public static string OsVersion()
    {
        try
        {
            var v = Environment.OSVersion.Version;
            return v.Build > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", v.Major, v.Minor, v.Build)
                : string.Format(CultureInfo.InvariantCulture, "{0}.{1}", v.Major, v.Minor);
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Process-independent machine architecture: <c>arm64</c>, <c>x64</c>, <c>x86</c>. Reported
    /// because an Apple-silicon machine and an Intel one are different support stories — an
    /// emulated container is the usual cause of "why is my solve slow".
    /// </summary>
    public static string Architecture()
    {
        try
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case System.Runtime.InteropServices.Architecture.Arm64: return "arm64";
                case System.Runtime.InteropServices.Architecture.X64: return "x64";
                case System.Runtime.InteropServices.Architecture.X86: return "x86";
                case System.Runtime.InteropServices.Architecture.Arm: return "arm";
                default: return RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
            }
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// The UI language Rhino is running in, as a BCP-47 tag Umami's language column understands
    /// (<c>en-US</c>, <c>de-DE</c>). The invariant culture has an empty name and is reported as
    /// <c>en-US</c> rather than as an empty string.
    /// </summary>
    public static string Language()
    {
        try
        {
            var name = CultureInfo.CurrentUICulture.Name;
            return string.IsNullOrWhiteSpace(name) ? "en-US" : name;
        }
        catch
        {
            return "en-US";
        }
    }

    /// <summary>
    /// The platform token for a browser-shaped User-Agent — the parenthesised part of
    /// <c>Mozilla/5.0 (…)</c>. What a real Chrome on that OS sends, because Umami matches it with
    /// regexes and anything else parses as <c>unknown</c>; see the class remarks.
    /// </summary>
    public static string UserAgentPlatform()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Chrome and Safari both still say "Intel Mac OS X 10_15_7" on Apple silicon and
                // on every macOS past 10.15 — the version was frozen for fingerprinting reasons.
                // detect-browser reads the real version fine too, but it reports a name either
                // way, so sending what a browser sends costs nothing and matches every other
                // consumer of this header.
                return "Macintosh; Intel Mac OS X 10_15_7";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64
                    ? "X11; Linux aarch64"
                    : "X11; Linux x86_64";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var v = Environment.OSVersion.Version;

                // Windows 11 reports itself as NT 10.0 to every browser; detect-browser has no
                // rule for anything above 10.0 and would answer "unknown" if we invented one.
                var nt = v.Major >= 10
                    ? "10.0"
                    : string.Format(CultureInfo.InvariantCulture, "{0}.{1}", v.Major, v.Minor);

                return "Windows NT " + nt + "; Win64; x64";
            }
        }
        catch
        {
            // Fall through to the neutral token.
        }

        return "Windows NT 10.0; Win64; x64";
    }
}
