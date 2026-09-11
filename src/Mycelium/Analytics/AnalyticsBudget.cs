using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

#nullable enable

namespace Mycelium.Analytics
{
    /// <summary>
    /// Machine-specific send ledger for <see cref="Analytics"/>: a calendar-day record of what
    /// has already been reported, plus a running monthly event count kept for observability.
    /// </summary>
    /// <remarks>
    /// Ported from Eddy3D's <c>GUI.Analytics.AnalyticsBudget</c> (Eddy3D-Dev/Eddy3D). That class
    /// enforced a monthly cap to protect Umami Cloud's 100k-events/month free tier; Mycelium
    /// reports to a self-hosted instance with no such billing ceiling, so no cap is enforced
    /// here either. <see cref="TryClaimDaily"/> is the mechanism that still matters: a key (a
    /// ribbon tab, the startup hit, a run) is sent at most once per machine per UTC day, so a
    /// Views figure is a count of machine-days rather than how often someone reopens a
    /// definition — that has nothing to do with billing.
    /// State lives next to the plugin's own app-data folder (<c>%APPDATA%/Mycelium</c>,
    /// <c>~/Library/Application Support/Mycelium</c> on macOS under net7.0). If the file cannot
    /// be read or written the ledger degrades to an in-memory one for the current process.
    /// </remarks>
    public static class AnalyticsBudget
    {
        private static readonly object Gate = new object();

        private static readonly string DefaultStatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mycelium", "analytics.json");

        private static string _statePath = DefaultStatePath;

        /// <summary>UTC clock. Test hook — the day and month keys are derived from it.</summary>
        public static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        // Mirrors the on-disk state. Also *is* the state when the disk is unusable.
        private static string _month = "";
        private static int _spent;
        private static bool _identified;
        private static string _identifiedVersion = "";
        private static string _day = "";
        private static readonly HashSet<string> _sentToday = new HashSet<string>(StringComparer.Ordinal);
        private static bool _loaded;

        /// <summary>Calendar month key. UTC so a timezone change cannot hand out a second budget.</summary>
        private static string CurrentMonth => Clock().ToString("yyyy-MM");

        /// <summary>Calendar day key, UTC for the same reason.</summary>
        private static string CurrentDay => Clock().ToString("yyyy-MM-dd");

        /// <summary>
        /// Records <paramref name="cost"/> events against this month's running count. Always
        /// succeeds (no monthly cap on the self-hosted instance); the bool return is kept so
        /// call sites that check it don't need to change.
        /// </summary>
        /// <param name="cost">Events the payload will cost (1 hit + 1 per data property).</param>
        /// <returns><c>true</c> unless <paramref name="cost"/> is not positive.</returns>
        public static bool TryConsume(int cost)
        {
            if (cost <= 0) return false;

            lock (Gate)
            {
                Load();
                _spent += cost;
                Save();
                return true;
            }
        }

        /// <summary>
        /// Claims the once-per-machine-per-day send for <paramref name="key"/>, recording
        /// <paramref name="cost"/> against the monthly count in the same step.
        /// </summary>
        /// <param name="key">What is being reported — <c>startup</c>, <c>tab/vegetation</c>,
        /// <c>run/tab/mycelium/generate</c>. Case-sensitive.</param>
        /// <param name="cost">Events the payload will cost.</param>
        /// <returns><c>true</c> for the first caller today with this key; <c>false</c> means
        /// already sent today, and the caller drops the payload.</returns>
        public static bool TryClaimDaily(string key, int cost = 1)
        {
            if (string.IsNullOrWhiteSpace(key) || cost <= 0) return false;

            lock (Gate)
            {
                Load();
                if (_sentToday.Contains(key)) return false;

                _sentToday.Add(key);
                _spent += cost;
                Save();
                return true;
            }
        }

        /// <summary>Whether <paramref name="key"/> has already been claimed today. Diagnostics only.</summary>
        public static bool SentToday(string key)
        {
            lock (Gate)
            {
                Load();
                return _sentToday.Contains(key);
            }
        }

        /// <summary>
        /// Claims the single <c>identify</c> send allowed per machine per month, recording
        /// <paramref name="cost"/> against the same running count.
        /// </summary>
        /// <param name="cost">Events the identify payload will cost.</param>
        /// <param name="version">Plugin version the payload will report. A machine that upgrades
        /// mid-month re-arms the claim, or the profile keeps reporting the version installed when
        /// the month began for up to 31 days after the upgrade.</param>
        /// <returns><c>true</c> for the first caller this month at this version.</returns>
        public static bool TryClaimIdentify(int cost, string version = "")
        {
            lock (Gate)
            {
                Load();
                if (_identified && _identifiedVersion == version) return false;

                _spent += cost;
                _identified = true;
                _identifiedVersion = version;
                Save();
                return true;
            }
        }

        /// <summary>Events reported this month. Diagnostics only — nothing is capped against it.</summary>
        public static int Spent
        {
            get { lock (Gate) { Load(); return _spent; } }
        }

        /// <summary>Discards the cached state so the next call re-reads disk. Test hook.</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                _loaded = false;
                _month = "";
                _spent = 0;
                _identified = false;
                _identifiedVersion = "";
                _day = "";
                _sentToday.Clear();
            }
        }

        /// <summary>
        /// Points the ledger at a different state file (<c>null</c> restores the default) and
        /// discards the cached state. Test hook.
        /// </summary>
        public static void UseStateFile(string? path)
        {
            lock (Gate)
            {
                _statePath = string.IsNullOrWhiteSpace(path) ? DefaultStatePath : path!;
            }

            Reset();
        }

        // Caller must hold Gate.
        private static void Load()
        {
            if (!_loaded)
            {
                ReadFromDisk();
                _loaded = true;
            }

            bool changed = false;

            // Month rollover resets the budget and re-arms the identify send.
            if (_month != CurrentMonth)
            {
                _month = CurrentMonth;
                _spent = 0;
                _identified = false;
                _identifiedVersion = "";
                changed = true;
            }

            // Day rollover forgets what was reported yesterday.
            if (_day != CurrentDay)
            {
                _day = CurrentDay;
                _sentToday.Clear();
                changed = true;
            }

            if (changed) Save();
        }

        // Caller must hold Gate. A missing/corrupt file simply starts a fresh month.
        private static void ReadFromDisk()
        {
            try
            {
                if (!File.Exists(_statePath)) return;

                var json = JsonNode.Parse(File.ReadAllText(_statePath)) as JsonObject;
                if (json is null) return;

                _month = json["month"]?.GetValue<string>() ?? "";
                _spent = json["spent"]?.GetValue<int>() ?? 0;
                _identified = json["identified"]?.GetValue<bool>() ?? false;
                _identifiedVersion = json["identified_version"]?.GetValue<string>() ?? "";
                _day = json["day"]?.GetValue<string>() ?? "";

                _sentToday.Clear();
                if (json["sent"] is JsonArray sent)
                {
                    foreach (var item in sent)
                    {
                        var key = item?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(key)) _sentToday.Add(key!);
                    }
                }
            }
            catch
            {
                // Unreadable state: fall back to the in-memory ledger for this process.
            }
        }

        // Caller must hold Gate. Failures are ignored — the in-memory ledger still caps the process.
        private static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_statePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var json = new JsonObject
                {
                    ["month"] = _month,
                    ["spent"] = _spent,
                    ["identified"] = _identified,
                    ["identified_version"] = _identifiedVersion,
                    ["day"] = _day,
                    ["sent"] = new JsonArray(_sentToday.OrderBy(k => k, StringComparer.Ordinal)
                        .Select(k => (JsonNode?)JsonValue.Create(k)).ToArray())
                };

                File.WriteAllText(_statePath, json.ToJsonString());
            }
            catch
            {
                // Read-only or roaming-profile failures must never break the plugin.
            }
        }
    }
}
