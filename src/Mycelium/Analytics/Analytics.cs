using System;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

#nullable enable

namespace Mycelium.Analytics
{
    /// <summary>
    /// Privacy-first usage analytics via Umami — ported from Eddy3D's <c>GUI.Analytics.Analytics</c>
    /// (Eddy3D-Dev/Eddy3D). Answers ONE question: <b>which ribbon tab do people use</b> —
    /// Building Types, Vegetation, Terrain — not which component, and not how often they reopen
    /// a file.
    /// </summary>
    /// <remarks>
    /// <para><b>What is sent, and what a number in the dashboard means.</b> Everything is
    /// reported at most once per machine per UTC day (<see cref="AnalyticsBudget.TryClaimDaily"/>):</para>
    /// <list type="bullet">
    /// <item><c>/startup</c> — a page view the first time the plugin loads each day.</item>
    /// <item><c>/tab/&lt;tab&gt;</c> — a page view the first time each day an object from that
    /// ribbon tab is on a canvas — <see cref="TabUsage"/> is the hook, <see cref="TrackTabUse"/>
    /// the sink.</item>
    /// <item><c>/tab/&lt;tab&gt;/&lt;action&gt;</c> — an event the first time each day a
    /// generator actually produces massing/terrain/street output. Opening a definition is not
    /// using it; running one is.</item>
    /// </list>
    /// <para><b>Umami bills per hit AND per stored data property</b> ("Each website hit counts
    /// as one event. If you save event data, each data property stored counts as one event." —
    /// docs.umami.is/docs/cloud/faq). Two consequences that must not be undone:</para>
    /// <list type="bullet">
    /// <item>The visitor hash rides in the <b>User-Agent</b>, not in <c>data</c> — free, and one
    /// of the three inputs to Umami's session id.</item>
    /// <item>Low-cardinality dimensions (tab, action) belong in the <b>url path</b>, not
    /// <c>data</c>.</item>
    /// </list>
    /// <para>Opt-out: set the environment variable <c>MYCELIUM_ANALYTICS=0</c> before starting
    /// Rhino (<see cref="OptOutVariable"/>).</para>
    /// </remarks>
    public static class Analytics
    {
        // Self-hosted Umami — the same instance Eddy3D reports to, one "website" per
        // Eddy3D-Dev/MyceliumGH-Dev product. This id is Mycelium's website on that instance.
        private const string UmamiEndpoint = "https://umami-theta-drab.vercel.app/api/send";

        private const string WebsiteId = "7cdd143e-2b40-4832-804f-07aaac80c238";

        /// <summary>
        /// Environment variable that disables every send when set to <c>0</c>, <c>false</c>,
        /// <c>off</c> or <c>no</c>. Read once, when the plugin first touches analytics.
        /// </summary>
        public const string OptOutVariable = "MYCELIUM_ANALYTICS";

        private const string UnknownSlug = "unknown";

        // 1 hit + the profile payload's data properties (rhino_version, mycelium_version, os,
        // os_version, arch, and network_org when the lookup succeeds).
        private const int ProfileCost = 7;

        // The identify payload carries the same property set.
        private const int IdentifyCost = ProfileCost;

        // Use SocketsHttpHandler to disable proxy detection which reads system config
        private static readonly HttpClient HttpClient = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false, // Critical: Don't read system proxy settings
            ConnectTimeout = TimeSpan.FromSeconds(5)
        });

        /// <summary>
        /// Configuration for the analytics endpoint.
        /// </summary>
        public static class Config
        {
            /// <summary>Gets or sets the Umami API endpoint URL.</summary>
            public static string Endpoint { get; set; } = UmamiEndpoint;

            /// <summary>Gets or sets the Umami website ID.</summary>
            public static string WebsiteId { get; set; } = Analytics.WebsiteId;

            /// <summary>Gets or sets whether tracking is enabled. Defaults from <see cref="OptOutVariable"/>.</summary>
            public static bool Enabled { get; set; } =
                ReadEnabledFromEnvironment(Environment.GetEnvironmentVariable(OptOutVariable));
        }

        /// <summary>
        /// Interprets the <see cref="OptOutVariable"/> value: unset or anything else means
        /// enabled; <c>0</c>, <c>false</c>, <c>off</c>, <c>no</c> (any case) mean disabled.
        /// </summary>
        public static bool ReadEnabledFromEnvironment(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            var v = value.Trim();
            return !(v.Equals("0", StringComparison.OrdinalIgnoreCase)
                     || v.Equals("false", StringComparison.OrdinalIgnoreCase)
                     || v.Equals("off", StringComparison.OrdinalIgnoreCase)
                     || v.Equals("no", StringComparison.OrdinalIgnoreCase));
        }

        // ── What Mycelium reports ───────────────────────────────────────────────────────────

        /// <summary>
        /// The daily <c>/startup</c> page view — the denominator every tab share is read
        /// against. Called from <see cref="TabUsage.Install"/>, i.e. from the plugin's priority
        /// load.
        /// </summary>
        public static void TrackStartup()
        {
            SendDaily("startup", "/startup", "Startup", eventName: null);
        }

        /// <summary>
        /// The daily <c>profile</c> event at <c>/startup</c> — Rhino build, plugin build, operating
        /// system, architecture and network organisation, as event PROPERTIES.
        /// </summary>
        /// <remarks>
        /// <para>These facts used to travel only in the once-a-month <c>identify</c> payload, and in
        /// the dashboard they were effectively absent. Two reasons, and the fix has to answer both.
        /// An <c>identify</c> writes SESSION data, and a session is keyed on the IP among other
        /// things — so a laptop that moves between campus, home and a cafe opens a new session on
        /// each network while the monthly claim has already been spent on whichever one happened to
        /// be active first. Every other session that month carries no properties at all. And Umami's
        /// Properties view groups event data by event NAME, so even a property that does land has
        /// nowhere to surface unless some named event carries it.</para>
        /// <para>So the facts ride here, on a named event, once per machine per UTC day: the same
        /// cadence as every other hit, which keeps a number in this breakdown a count of machine-days
        /// exactly like the rest of the dashboard. <c>/startup</c> stays a page view and keeps its
        /// job as the denominator — an event with a name is not counted as one, so a named
        /// <c>/startup</c> would have quietly zeroed it.</para>
        /// </remarks>
        public static void TrackProfile()
        {
            if (!Config.Enabled) return;
            if (!AnalyticsBudget.TryClaimDaily("profile", ProfileCost)) return;

            Task.Run(async () =>
            {
                try
                {
                    var data = await BuildProfileDataAsync();
                    await SendPayloadAsync("/startup", "profile", "Profile", data, budgetClaimed: true);
                }
                catch
                {
                    // Best-effort; the daily claim is spent either way.
                }
            });
        }

        /// <summary>
        /// Reports that an object from the ribbon tab <paramref name="subCategory"/> is on a
        /// canvas, as a <c>/tab/&lt;slug&gt;</c> page view — once per machine per day. Anything
        /// that does not slug to a tab is ignored.
        /// </summary>
        public static void TrackTabUse(string? subCategory)
        {
            var slug = TabSlug(subCategory);
            if (slug == UnknownSlug) return;

            SendDaily("tab/" + slug, "/tab/" + slug, "Tab: " + PanelName(subCategory), eventName: null);
        }

        /// <summary>
        /// Reports that an action was actually run on a tab — the difference between opening a
        /// definition and using it. Sent as an event named <c>&lt;action&gt;-run</c> at
        /// <c>/tab/&lt;tab&gt;/&lt;action&gt;</c>, once per machine per day per combination.
        /// </summary>
        /// <param name="tab">The ribbon tab the run belongs to (a panel string or just its name).</param>
        /// <param name="action">What happened, e.g. <c>generate</c>.</param>
        public static void TrackRun(string? tab, string action)
        {
            var t = TabSlug(tab);
            var a = Slug(action);
            var url = $"/tab/{t}/{a}";

            SendDaily("run" + url, url, $"Run: {PanelName(tab)} {a}", eventName: a + "-run");
        }

        /// <summary>
        /// One send per machine per UTC day for <paramref name="key"/>. The claim is taken
        /// synchronously (a hash lookup, plus a small file write the first time each day) so the
        /// caller's thread decides; the network round trip is off-thread and best-effort.
        /// </summary>
        private static void SendDaily(string key, string url, string title, string? eventName)
        {
            if (!Config.Enabled) return;
            if (!AnalyticsBudget.TryClaimDaily(key, 1)) return;

            Task.Run(async () =>
            {
                try
                {
                    await SendPayloadAsync(url, eventName, title, null, budgetClaimed: true);
                }
                catch
                {
                    // Best-effort; the daily claim is spent either way.
                }
            });
        }

        // ── Slugs ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The URL segment for a ribbon panel: <c>"Building Types"</c> → <c>building-types</c>.
        /// The number prefix, if any, is dropped. Returns <c>unknown</c> for anything empty.
        /// </summary>
        public static string TabSlug(string? subCategory)
        {
            var name = PanelName(subCategory);
            if (string.IsNullOrWhiteSpace(name)) return UnknownSlug;
            return Slug(name.Replace("+", " plus"));
        }

        /// <summary>
        /// A panel string without its <c>NN | </c> prefix, if any. A string that carries no such
        /// prefix comes back trimmed and otherwise untouched.
        /// </summary>
        public static string PanelName(string? subCategory)
        {
            if (subCategory == null) return "";
            var text = subCategory.Trim();

            int bar = text.IndexOf('|');
            if (bar > 0)
            {
                bool numericPrefix = true;
                for (int i = 0; i < bar; i++)
                {
                    if (!char.IsDigit(text[i]) && !char.IsWhiteSpace(text[i]))
                    {
                        numericPrefix = false;
                        break;
                    }
                }

                if (numericPrefix) text = text.Substring(bar + 1).Trim();
            }

            return text;
        }

        /// <summary>
        /// Normalizes a value for use as a URL path segment (lowercase, runs of anything that is
        /// not a letter or digit become one hyphen). Keeps the reported paths low-cardinality so
        /// they stay groupable in Umami.
        /// </summary>
        private static string Slug(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return UnknownSlug;

            var builder = new StringBuilder(value!.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
                else if (builder.Length > 0 && builder[builder.Length - 1] != '-') builder.Append('-');
            }

            string slug = builder.ToString().Trim('-');
            return slug.Length > 0 ? slug : UnknownSlug;
        }

        // ── Generic senders (kept for ad-hoc use) ──────────────────────────────────────────

        /// <summary>
        /// Tracks an event in Umami analytics.
        /// </summary>
        public static void TrackEvent(
            string eventName,
            string url = "/",
            JsonObject? additionalData = null,
            Action<string>? callback = null)
        {
            if (!Config.Enabled)
            {
                callback?.Invoke("Tracking disabled");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    string result = await TrackEventAsync(eventName, url, additionalData);
                    callback?.Invoke(result);
                }
                catch (Exception ex)
                {
                    callback?.Invoke($"Error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Tracks a page view in Umami analytics asynchronously.
        /// </summary>
        public static async Task<string> TrackPageViewAsync(string url, string title)
        {
            return await SendPayloadAsync(url, null, title, null);
        }

        /// <summary>
        /// Tracks an event in Umami analytics asynchronously.
        /// </summary>
        public static async Task<string> TrackEventAsync(
            string eventName,
            string url = "/",
            JsonObject? additionalData = null)
        {
            return await SendPayloadAsync(url, eventName, eventName, additionalData);
        }

        /// <summary>
        /// Shared logic to send payloads to Umami. If eventName is null, it is treated as a
        /// Page View.
        /// </summary>
        /// <param name="budgetClaimed">True when the caller already charged the monthly budget
        /// (the daily ledger claims and charges in one step); false charges it here.</param>
        private static async Task<string> SendPayloadAsync(
            string url,
            string? eventName,
            string title,
            JsonObject? additionalData,
            bool budgetClaimed = false)
        {
            if (!Config.Enabled) return "Tracking disabled";

            try
            {
                string visitorId = Identity.GetUniqueId();
                string userAgent = BuildUserAgent(visitorId);

                // 1. Send IDENTIFY request (Session Setup) — once per machine per DAY. Daily rather
            // than monthly: the session an identify attaches to is keyed on the IP, so one
            // send a month lands on one network's session and leaves every other session
            // that machine opens without any properties at all.
                string myceliumVersion = GetPluginVersion();
                if (AnalyticsBudget.TryClaimDaily("identify/" + myceliumVersion, IdentifyCost))
                {
                    await SendIdentifyAsync(url, eventName, title, userAgent);
                }

                // 2. Send DATA request (Event or PageView)
                var dataObject = new JsonObject();
                if (additionalData != null)
                {
                    // JsonNode instances are single-parent; net7.0 lacks JsonNode.DeepClone (net8+
                    // only), so round-trip through text to reattach the value to dataObject.
                    foreach (var prop in additionalData)
                        dataObject[prop.Key] = prop.Value is null ? null : JsonNode.Parse(prop.Value.ToJsonString());
                }

                // Umami bills the hit plus one event per stored data property.
                if (!budgetClaimed)
                {
                    int cost = 1 + dataObject.Count;
                    if (!AnalyticsBudget.TryConsume(cost))
                    {
                        return "Skipped: monthly analytics budget spent";
                    }
                }

                var payloadData = new JsonObject
                {
                    ["website"] = Config.WebsiteId,
                    ["hostname"] = "mycelium-plugin",
                    ["language"] = HostInfo.Language(),
                    ["screen"] = HostInfo.Screen,
                    ["url"] = url,
                    ["referrer"] = "https://mycelium-gh.netlify.app",
                    ["title"] = title
                };

                // An empty data object would still be sent as a property bag; omit it entirely.
                if (dataObject.Count > 0)
                {
                    payloadData["data"] = dataObject;
                }

                if (!string.IsNullOrEmpty(eventName))
                {
                    payloadData["name"] = eventName;
                }

                var payload = new JsonObject
                {
                    ["type"] = "event",
                    ["payload"] = payloadData
                };

                var request = new HttpRequestMessage(HttpMethod.Post, Config.Endpoint);
                request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
                request.Headers.UserAgent.ParseAdd(userAgent);

                var response = await HttpClient.SendAsync(request);

                return response.IsSuccessStatusCode
                    ? $"Success: {(eventName ?? "Page View")} tracked"
                    : $"Failed: {response.StatusCode}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// The machine profile both <see cref="TrackProfile"/> and the <c>identify</c> payload report:
        /// Rhino build, plugin build, OS family and version, architecture, and the network
        /// organisation when the lookup answers. One builder so the two can never drift — a property
        /// present on the event and absent from the session profile reads as a broken dashboard.
        /// </summary>
        private static async Task<JsonObject> BuildProfileDataAsync()
        {
            var data = new JsonObject
            {
                ["rhino_version"] = GetRhinoVersion(),
                ["mycelium_version"] = GetPluginVersion(),
                ["os"] = HostInfo.OsFamily(),
                ["os_version"] = HostInfo.OsVersion(),
                ["arch"] = HostInfo.Architecture()
            };

            // The network organisation ("Georgia Tech", "T-Mobile") is what says whether a machine is
            // an institutional install. It is a network round trip, so it is resolved here — once a
            // day, on the sender's own thread — and omitted entirely when the lookup cannot answer.
            string? networkOrg = await GetNetworkOrgAsync();
            if (!string.IsNullOrWhiteSpace(networkOrg))
            {
                data["network_org"] = networkOrg;
            }

            return data;
        }

        /// <summary>
        /// The network organisation behind this machine's public IP, via ipinfo.io — "Georgia Tech",
        /// "T-Mobile". It is the only signal that separates an institutional deployment from a home
        /// install, which is what a funding question is actually about. Returns null on any failure:
        /// analytics must never break the plugin, and an absent property is honest about that.
        /// </summary>
        private static async Task<string?> GetNetworkOrgAsync()
        {
            try
            {
                var response = await HttpClient.GetStringAsync("https://ipinfo.io/json");
                var json = JsonNode.Parse(response);
                string org = json?["org"]?.ToString() ?? "";
                return !string.IsNullOrWhiteSpace(org) ? org : "";
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Sends the once-per-month <c>identify</c> payload describing this machine. The caller
        /// has already reserved <see cref="IdentifyCost"/> from the budget.
        /// </summary>
        private static async Task SendIdentifyAsync(
            string url, string? eventName, string title, string userAgent)
        {
            try
            {
                var identifyData = await BuildProfileDataAsync();

                var identifyPayload = new JsonObject
                {
                    ["type"] = "identify",
                    ["payload"] = new JsonObject
                    {
                        ["website"] = Config.WebsiteId,
                        ["hostname"] = "mycelium-plugin",
                        ["language"] = HostInfo.Language(),
                        ["screen"] = HostInfo.Screen,
                        ["url"] = url,
                        ["referrer"] = "https://mycelium-gh.netlify.app",
                        ["title"] = title,
                        ["name"] = eventName,
                        ["data"] = identifyData
                    }
                };

                var identifyRequest = new HttpRequestMessage(HttpMethod.Post, Config.Endpoint);
                identifyRequest.Content =
                    new StringContent(identifyPayload.ToJsonString(), Encoding.UTF8, "application/json");
                identifyRequest.Headers.UserAgent.ParseAdd(userAgent);

                await HttpClient.SendAsync(identifyRequest);
            }
            catch
            {
                // Identify is best-effort; the follow-up hit still carries the visitor via the UA.
            }
        }

        /// <summary>
        /// Builds the User-Agent header. The visitor hash is appended here rather than stored in
        /// <c>data</c> because the header is part of the hit and therefore free, and because
        /// Umami derives its session id from exactly three inputs plus a salt.
        /// </summary>
        public static string BuildUserAgent(string visitorId)
        {
            return $"Mozilla/5.0 ({HostInfo.UserAgentPlatform()}) AppleWebKit/537.36 (KHTML, like Gecko) " +
                   $"Chrome/120.0.0.0 Safari/537.36 Mycelium/1.0 (id:{visitorId})";
        }

        /// <summary>
        /// Gets the Rhino version if available.
        /// </summary>
        private static string GetRhinoVersion()
        {
            try
            {
                return Rhino.RhinoApp.Version.ToString();
            }
            catch
            {
                return "unknown";
            }
        }

        /// <summary>
        /// Gets the Mycelium release version as a bare version string, preferring the
        /// SourceLink-free informational version so a pre-release suffix is reported as cut.
        /// </summary>
        private static string GetPluginVersion()
        {
            try
            {
                var assembly = typeof(Analytics).Assembly;

                string? informational = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

                if (!string.IsNullOrWhiteSpace(informational))
                {
                    int plus = informational.IndexOf('+');
                    if (plus >= 0) informational = informational.Substring(0, plus);
                    if (!string.IsNullOrWhiteSpace(informational)) return informational.Trim();
                }

                return assembly.GetName().Version?.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
