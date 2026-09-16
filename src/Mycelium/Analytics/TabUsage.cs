using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Grasshopper;
using Grasshopper.Kernel;

#nullable enable

namespace Mycelium.Analytics
{
    /// <summary>
    /// Reports which Mycelium <b>ribbon tabs</b> a machine uses, by watching every Grasshopper
    /// document for objects whose category is <see cref="Category"/> and handing their
    /// <c>SubCategory</c> to <see cref="Reporter"/>. The reporter — <see cref="Analytics.TrackTabUse"/>
    /// in production — turns that into one <c>/tab/&lt;slug&gt;</c> page view per machine per day.
    /// </summary>
    /// <remarks>
    /// Ported from Eddy3D's <c>GUI.Analytics.TabUsage</c> (Eddy3D-Dev/Eddy3D), which documents the
    /// full rationale: a document-server hook (not a base-class <c>AddedToDocument</c> override)
    /// sees every object regardless of which class it derives from, and both the
    /// <c>DocumentAdded</c> event and an explicit scan of already-open documents are needed to
    /// catch objects that arrived from a loaded file. Call <see cref="Install"/> once from the
    /// plugin's <c>GH_AssemblyPriority</c> (<c>MyceliumUpdateNotifier</c>); it is idempotent and
    /// never throws.
    /// </remarks>
    public static class TabUsage
    {
        /// <summary>The Grasshopper category every Mycelium component registers under.</summary>
        public const string Category = "Mycelium";

        private static readonly object Gate = new object();
        private static readonly object Marker = new object();
        private static bool _installed;

        // Documents currently subscribed. Weak so a document that was closed without a
        // DocumentRemoved (the server is not the only owner) cannot be kept alive by this table.
        private static readonly ConditionalWeakTable<GH_Document, object> Hooked = new ConditionalWeakTable<GH_Document, object>();

        /// <summary>
        /// Receives the raw <c>SubCategory</c> of every Mycelium object observed. Defaults to
        /// <see cref="Analytics.TrackTabUse"/>; tests replace it to capture the stream.
        /// </summary>
        public static Action<string>? Reporter { get; set; } = Analytics.TrackTabUse;

        /// <summary>Whether <see cref="Install"/> has run in this process.</summary>
        public static bool IsInstalled
        {
            get { lock (Gate) return _installed; }
        }

        /// <summary>
        /// Subscribes to the document server (once per process), scans the documents already
        /// open, and sends the daily <c>/startup</c> hit. Safe to call from the plugin's
        /// <c>GH_AssemblyPriority.PriorityLoad</c>.
        /// </summary>
        public static void Install()
        {
            lock (Gate)
            {
                if (_installed) return;
                _installed = true;
            }

            try
            {
                var server = Instances.DocumentServer;
                server.DocumentAdded += OnDocumentAdded;
                server.DocumentRemoved += OnDocumentRemoved;

                // A plugin reloaded into a running Grasshopper already has documents to look at.
                for (int i = 0; i < server.DocumentCount; i++) Hook(server[i]);
            }
            catch
            {
                // Analytics must never break plugin loading.
            }

            // Eto answers from AppKit on macOS, so the screen has to be read on the UI thread — this
            // is it. Every payload afterwards reports the cached value; Umami has no other input
            // for the device class, so a machine whose probe fails is a desktop by default rather
            // than by measurement.
            HostInfo.ProbeScreen();

            Analytics.TrackStartup();
            Analytics.TrackProfile();
        }

        private static void OnDocumentAdded(GH_DocumentServer sender, GH_Document doc) => Hook(doc);

        private static void OnDocumentRemoved(GH_DocumentServer sender, GH_Document doc)
        {
            try
            {
                if (doc == null) return;
                doc.ObjectsAdded -= OnObjectsAdded;
                Hooked.Remove(doc);
            }
            catch
            {
                // Best-effort unhook.
            }
        }

        private static void Hook(GH_Document? doc)
        {
            if (doc == null) return;

            try
            {
                if (!Hooked.TryAdd(doc, Marker)) return;
                doc.ObjectsAdded += OnObjectsAdded;

                // The objects a file brought in arrive without an ObjectsAdded — see the class remarks.
                Observe(doc.Objects.ToArray());
            }
            catch
            {
                // Never let a bad document break the canvas.
            }
        }

        private static void OnObjectsAdded(object sender, GH_DocObjectEventArgs e)
        {
            try
            {
                Observe(e?.Objects);
            }
            catch
            {
                // Never let analytics throw into Grasshopper's event dispatch.
            }
        }

        /// <summary>
        /// Hands the <c>SubCategory</c> of every Mycelium object in <paramref name="objects"/> to
        /// <see cref="Reporter"/>. Public so a test can drive it without a document.
        /// </summary>
        public static void Observe(IEnumerable<IGH_DocumentObject>? objects)
        {
            if (objects == null) return;
            var reporter = Reporter;
            if (reporter == null) return;

            foreach (var obj in objects)
            {
                if (IsMycelium(obj)) reporter(obj.SubCategory);
            }
        }

        /// <summary>
        /// A Mycelium object is anything registered under <see cref="Category"/> with a ribbon
        /// panel. Params and components alike — the question is which TAB it came from, not what
        /// it is.
        /// </summary>
        public static bool IsMycelium(IGH_DocumentObject? obj)
        {
            if (obj == null) return false;
            try
            {
                return string.Equals(obj.Category, Category, StringComparison.Ordinal)
                       && !string.IsNullOrWhiteSpace(obj.SubCategory);
            }
            catch
            {
                return false;
            }
        }
    }
}
