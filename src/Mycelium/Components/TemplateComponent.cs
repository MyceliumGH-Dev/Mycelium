using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Mycelium.Core;

namespace Mycelium.Components
{
    /// <summary>
    /// Template browser adopted from Eddy3D's Select Template component: syncs the template
    /// list from the Mycelium-Templates GitHub repository, caches it locally, downloads
    /// definitions on demand, and merges the chosen template into the current document.
    /// User folders / GitHub URLs from the Directory input are offered alongside.
    /// </summary>
    public class TemplateComponent : GH_Component
    {
        // --- GitHub repository configuration ---
        private static readonly string RepoOwner = "SustainableUrbanSystemsLab";
        private static readonly string RepoName = "Mycelium-Templates";
        // Mutable: falls back to "main" once if the version branch doesn't exist yet.
        private static string RepoBranch = GetBranchFromVersion();
        // ----------------------------------------

        /// <summary>
        /// Converts the assembly version (e.g. "0.1.0.0") to a Mycelium-Templates branch name.
        /// Mirrors Eddy3D: templates branch per exact release version. Falls back to "main"
        /// if the version can't be read (e.g. running unbuilt from a debugger).
        /// </summary>
        private static string GetBranchFromVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version == null) return "main";

            var result = version.ToString();
            return string.IsNullOrWhiteSpace(result) ? "main" : result;
        }

        private TemplateCache _cache = new TemplateCache();
        private bool _isFetching;
        public bool IsFetching => _isFetching;
        private bool _isCheckingForUpdate;
        private bool _updateAvailable;
        public bool UpdateAvailable => _updateAvailable;
        private string _errorMessage;
        public string ErrorMessage => _errorMessage;

        private readonly Dictionary<string, bool> _externalFetchStates = new Dictionary<string, bool>();

        internal string MainRepoDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Mycelium", "Templates", "GitHub");

        private string ExternalRepoRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Mycelium", "Templates", "External");

        private sealed class TemplateCache
        {
            public List<string> Files { get; set; } = new List<string>();
            public string LastSyncedSha { get; set; }
        }

        public TemplateComponent()
          : base("Mycelium Templates", "Templates",
              "Load example Grasshopper definitions for common Mycelium workflows.\n\n" +
              "Templates are synced from the Mycelium-Templates GitHub repository; " +
              "your own folders and GitHub URLs are offered alongside.",
              "Mycelium", "Utilities")
        {
        }

        // GUID predates the Mycelium rename; existing Grasshopper files depend on it.
        public override Guid ComponentGuid => new Guid("A1B2C3D4-5678-9ABC-DEF0-123456789ABC");

        protected override Bitmap Icon => ComponentIcons.Get("MyceliumTemplate");

        // Owner deliberately omitted: the full slug wraps past the label bounds on the canvas.
        public string TemplateSourceLabel => $"Templates @ {RepoBranch}";

        public override void CreateAttributes()
        {
            m_attributes = new TemplateComponentAttributes(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Directory", "Dir",
                "Additional template sources: local folder paths or GitHub repository URLs.",
                GH_ParamAccess.list);
            pManager[0].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Templates", "T",
                "Template file paths from all sources.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var additionalInputs = new List<string>();
            DA.GetDataList(0, additionalInputs);

            // 1. Sync the main repo cache
            if (_cache.Files.Count == 0 && !_isFetching && _errorMessage == null)
            {
                LoadTemplateCache();
                if (_cache.Files.Count == 0 && !_isFetching) FetchGithubFilesAsync();
            }

            // 2. Background update check for the main repo
            if (_cache.Files.Count > 0 && !_isFetching && !_isCheckingForUpdate && !_updateAvailable)
            {
                CheckForUpdatesAsync();
            }

            if (_updateAvailable)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Template update available - click 'Select Template' to sync.");
            else if (_isFetching || (_cache.Files.Count == 0 && _errorMessage == null))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Syncing templates from GitHub...");

            if (_errorMessage != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _errorMessage);

            // 3. Collect all template paths
            var allFiles = new List<string>();

            foreach (var file in _cache.Files)
                allFiles.Add(Path.Combine(MainRepoDir, file));

            foreach (var input in additionalInputs)
            {
                if (string.IsNullOrWhiteSpace(input)) continue;

                if (IsGitHubUrl(input, out var ghInfo))
                {
                    var externalDir = Path.Combine(ExternalRepoRoot, ghInfo.Owner, ghInfo.Repo, ghInfo.Branch ?? "HEAD");

                    if (!Directory.Exists(externalDir) || Directory.GetFiles(externalDir, "*.gh*", SearchOption.AllDirectories).Length == 0)
                    {
                        if (!_externalFetchStates.TryGetValue(input, out var isFetching) || !isFetching)
                        {
                            FetchExternalGithubFilesAsync(input, ghInfo);
                        }
                    }

                    if (Directory.Exists(externalDir))
                    {
                        foreach (var f in Directory.GetFiles(externalDir, "*.gh*", SearchOption.AllDirectories))
                        {
                            // If the URL has a subpath (/tree/branch/SubDir), filter by it
                            if (!string.IsNullOrEmpty(ghInfo.Path) && !f.Replace("\\", "/").Contains(ghInfo.Path)) continue;
                            allFiles.Add(f);
                        }
                    }
                }
                else if (Directory.Exists(input))
                {
                    try
                    {
                        var files = Directory.GetFiles(input, "*.gh*", SearchOption.AllDirectories)
                            .Where(f => f.EndsWith(".gh", StringComparison.OrdinalIgnoreCase)
                                     || f.EndsWith(".ghx", StringComparison.OrdinalIgnoreCase));
                        allFiles.AddRange(files);
                    }
                    catch { /* Ignore access errors */ }
                }
            }

            DA.SetDataList(0, allFiles);
        }

        // --- GitHub URL handling (ported from Eddy3D, incl. path-traversal guard) ---

        private struct GitHubInfo
        {
            public string Owner;
            public string Repo;
            public string Branch;
            public string Path;
        }

        private static bool IsGitHubUrl(string url, out GitHubInfo info)
        {
            info = new GitHubInfo();
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                return false;

            var parts = url.Substring("https://github.com/".Length).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;

            info.Owner = parts[0];
            info.Repo = parts[1];

            if (parts.Length >= 4 && parts[2] == "tree")
            {
                info.Branch = parts[3];
                if (parts.Length > 4)
                    info.Path = string.Join("/", parts.Skip(4));
            }
            else
            {
                // "HEAD" resolves to the repository's default branch on the GitHub API.
                info.Branch = "HEAD";
            }

            // Validate identifiers to prevent path traversal via malicious GitHub URLs.
            // Owner and Repo must be single identifiers; Branch may contain slashes.
            return IsValidGitHubIdentifier(info.Owner, allowSlashes: false)
                && IsValidGitHubIdentifier(info.Repo, allowSlashes: false)
                && IsValidGitHubIdentifier(info.Branch, allowSlashes: true);
        }

        private static bool IsValidGitHubIdentifier(string input, bool allowSlashes)
        {
            if (string.IsNullOrEmpty(input)) return false;

            var segments = allowSlashes ? input.Split('/') : new[] { input };
            foreach (var segment in segments)
            {
                if (string.IsNullOrEmpty(segment) || segment == "." || segment == "..") return false;
                if (!Regex.IsMatch(segment, @"^[a-zA-Z0-9._-]+$")) return false;
            }

            return true;
        }

        // --- Sync / cache ---

        private void LoadTemplateCache()
        {
            try
            {
                var cachePath = Path.Combine(MainRepoDir, "template_list.json");
                if (File.Exists(cachePath))
                {
                    var json = File.ReadAllText(cachePath);
                    _cache = JsonSerializer.Deserialize<TemplateCache>(json) ?? new TemplateCache();
                }
            }
            catch (Exception ex)
            {
                _errorMessage = "Failed to load local template cache: " + ex.Message;
            }
        }

        private async void FetchGithubFilesAsync()
        {
            var retryAgainstMain = false;
            _isFetching = true;
            _errorMessage = null;
            _updateAvailable = false;
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => ExpireSolution(true)));
            try
            {
                using (var lister = new GitHubFileLister())
                {
                    var latestSha = await lister.GetLatestCommitShaAsync(RepoOwner, RepoName, RepoBranch);
                    var files = await lister.ListFilesAsync(RepoOwner, RepoName, RepoBranch);

                    _cache.Files = files
                        .Where(f => f.EndsWith(".ghx", StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith(".gh", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    _cache.LastSyncedSha = latestSha;

                    if (!Directory.Exists(MainRepoDir)) Directory.CreateDirectory(MainRepoDir);
                    var cachePath = Path.Combine(MainRepoDir, "template_list.json");
                    File.WriteAllText(cachePath, JsonSerializer.Serialize(_cache));
                }
            }
            catch (Exception ex)
            {
                // A missing version branch (e.g. templates repo not yet branched for this
                // release) surfaces as a 404 — retry once against main before giving up.
                if (RepoBranch != "main")
                {
                    RepoBranch = "main";
                    retryAgainstMain = true;
                }
                else
                {
                    _errorMessage = "Failed to sync GitHub templates: " + ex.Message;
                }
            }
            finally
            {
                _isFetching = false;
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() => ExpireSolution(true)));
            }

            if (retryAgainstMain)
                FetchGithubFilesAsync();
        }

        private async void CheckForUpdatesAsync()
        {
            if (string.IsNullOrEmpty(_cache.LastSyncedSha)) return;

            _isCheckingForUpdate = true;
            try
            {
                using (var lister = new GitHubFileLister())
                {
                    var latestSha = await lister.GetLatestCommitShaAsync(RepoOwner, RepoName, RepoBranch);
                    if (latestSha != _cache.LastSyncedSha)
                    {
                        _updateAvailable = true;
                        Rhino.RhinoApp.InvokeOnUiThread((Action)(() => ExpireSolution(true)));
                    }
                }
            }
            catch
            {
                // Silently fail for the background check
            }
            finally
            {
                // Back off before allowing another check to avoid spamming on frequent expires
                await Task.Delay(60000);
                _isCheckingForUpdate = false;
            }
        }

        private async void FetchExternalGithubFilesAsync(string inputUrl, GitHubInfo info)
        {
            if (_externalFetchStates.TryGetValue(inputUrl, out var isFetching) && isFetching) return;
            _externalFetchStates[inputUrl] = true;

            try
            {
                using (var lister = new GitHubFileLister())
                {
                    var files = await lister.ListFilesAsync(info.Owner, info.Repo, info.Branch);
                    var validFiles = files.Where(f => f.EndsWith(".ghx", StringComparison.OrdinalIgnoreCase)
                                                   || f.EndsWith(".gh", StringComparison.OrdinalIgnoreCase));

                    var targetDir = Path.Combine(ExternalRepoRoot, info.Owner, info.Repo, info.Branch);
                    if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                    foreach (var relPath in validFiles)
                    {
                        if (!string.IsNullOrEmpty(info.Path) && !relPath.Replace("\\", "/").StartsWith(info.Path)) continue;

                        var localPath = Path.Combine(targetDir, relPath);
                        var localSub = Path.GetDirectoryName(localPath);
                        if (!Directory.Exists(localSub)) Directory.CreateDirectory(localSub);

                        await DownloadRawFileAsync(info.Owner, info.Repo, info.Branch, relPath, localPath);
                    }
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to fetch external templates from {inputUrl}: {ex.Message}");
            }
            finally
            {
                _externalFetchStates[inputUrl] = false;
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() => ExpireSolution(true)));
            }
        }

        private static async Task DownloadRawFileAsync(string owner, string repo, string branch, string relPath, string localPath)
        {
            var rawUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{relPath}";
            using (var client = new System.Net.Http.HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mycelium");
                var data = await client.GetByteArrayAsync(rawUrl);
                File.WriteAllBytes(localPath, data);
            }
        }

        private async Task<bool> EnsureTemplateDownloadedAsync(string relPath)
        {
            var localPath = Path.Combine(MainRepoDir, relPath);
            if (File.Exists(localPath)) return true;

            try
            {
                var localSubDir = Path.GetDirectoryName(localPath);
                if (!Directory.Exists(localSubDir)) Directory.CreateDirectory(localSubDir);

                await DownloadRawFileAsync(RepoOwner, RepoName, RepoBranch, relPath, localPath);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to download template: {ex.Message}\n\nPlease check your internet connection and try again.",
                    "Template Download Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void ClearMainTemplateCache()
        {
            try
            {
                if (Directory.Exists(MainRepoDir)) Directory.Delete(MainRepoDir, true);
                _cache = new TemplateCache();
            }
            catch (Exception ex)
            {
                _errorMessage = "Failed to clear local template cache: " + ex.Message;
            }
        }

        private void RefreshTemplates()
        {
            if (_isFetching) return;

            _errorMessage = null;
            ClearMainTemplateCache();
            if (_errorMessage == null)
                FetchGithubFilesAsync();
            else
                ExpireSolution(true);
        }

        // --- Canvas insertion ---

        /// <summary>
        /// Loads a template file and merges its objects into the active document,
        /// placed next to this component.
        /// </summary>
        private void InsertTemplate(string filePath)
        {
            var canvas = Grasshopper.Instances.ActiveCanvas;
            if (canvas == null || !canvas.Focused || !File.Exists(filePath))
                return;

            var io = new GH_DocumentIO();
            if (!io.Open(filePath))
            {
                MessageBox.Show(
                    "Failed to open the template document. Please check if the file is valid and accessible.",
                    "Template Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var templateDoc = io.Document;

            templateDoc.SelectAll();
            // New object ids avoid conflicts with anything already on the canvas
            templateDoc.MutateAllIds();

            var box = templateDoc.BoundingBox(false);
            templateDoc.TranslateObjects(GetInsertOffset(box.Location), true);
            templateDoc.ExpireSolution();

            var currentDoc = canvas.Document;
            currentDoc.DeselectAll();
            currentDoc.MergeDocument(templateDoc);
        }

        private Size GetInsertOffset(PointF fromLocation)
        {
            var moveX = Attributes.Bounds.Left - 80 - fromLocation.X;
            var moveY = Attributes.Bounds.Y + 180 - fromLocation.Y;
            return new Size(new Point(Convert.ToInt32(moveX), Convert.ToInt32(moveY)));
        }

        // --- Menus ---

        public void OpenLocalTemplateFolder()
        {
            if (!Directory.Exists(MainRepoDir)) Directory.CreateDirectory(MainRepoDir);
            // UseShellExecute opens the folder with the OS default handler (Explorer/Finder).
            Process.Start(new ProcessStartInfo(MainRepoDir) { UseShellExecute = true });
        }

        public void OpenGitHubRepository()
        {
            Process.Start(new ProcessStartInfo($"https://github.com/{RepoOwner}/{RepoName}/tree/{RepoBranch}") { UseShellExecute = true });
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            AppendTemplateMenuItems(menu);
        }

        public void AppendTemplateMenuItems(ToolStripDropDown menu)
        {
            var sourceItem = Menu_AppendItem(menu, TemplateSourceLabel, (s, e) => OpenGitHubRepository());
            sourceItem.ToolTipText = $"Templates are fetched from {RepoOwner}/{RepoName} ({RepoBranch}). Click to view on GitHub.";
            menu.Items.Add(new ToolStripSeparator());

            if (_isFetching)
            {
                var fetchingItem = menu.Items.Add("Fetching from GitHub...");
                fetchingItem.Enabled = false;
            }
            else if (_cache.Files.Count == 0)
            {
                var noItemsItem = menu.Items.Add("No templates found on GitHub");
                noItemsItem.Enabled = false;
                noItemsItem.ToolTipText = "The template repository might be empty or unavailable. Try 'Retry Fetch'.";
                var retryItem = Menu_AppendItem(menu, "🔄 Retry Fetch", (s, e) => FetchGithubFilesAsync());
                if (retryItem != null) retryItem.ToolTipText = "Attempt to fetch the template list from GitHub again.";
            }
            else
            {
                if (_updateAvailable)
                {
                    var updateItem = new ToolStripMenuItem("Update Available! Click to Sync", null, (s, e) => RefreshTemplates())
                    {
                        BackColor = Color.Gold,
                        ForeColor = Color.Black,
                        ToolTipText = "Clear the cached templates and synchronize the latest versions from GitHub."
                    };
                    menu.Items.Add(updateItem);
                    menu.Items.Add(new ToolStripSeparator());
                }

                foreach (var file in _cache.Files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var localPath = Path.Combine(MainRepoDir, file);
                    var isCached = File.Exists(localPath);
                    var label = isCached ? "📄 " + fileName : "📄 " + fileName + " (Click to Download)";

                    EventHandler onClick = async (s, e) =>
                    {
                        if (await EnsureTemplateDownloadedAsync(file))
                        {
                            InsertTemplate(localPath);
                            ExpireSolution(true);
                        }
                    };

                    var item = new ToolStripMenuItem(label, null, onClick)
                    {
                        ToolTipText = isCached
                            ? $"Load cached template from: {localPath}"
                            : $"Download template from GitHub and load it. Cached at: {localPath}"
                    };
                    menu.Items.Add(item);
                }
            }

            menu.Items.Add(new ToolStripSeparator());

            var openFolderItem = Menu_AppendItem(menu, "📁 Open Local Template Folder", (s, e) => OpenLocalTemplateFolder());
            if (openFolderItem != null) openFolderItem.ToolTipText = "Open the local directory where GitHub templates are cached.";

            var viewRepoItem = Menu_AppendItem(menu, "🌐 View GitHub Repository", (s, e) => OpenGitHubRepository());
            if (viewRepoItem != null) viewRepoItem.ToolTipText = "Open the template repository in your default browser.";
        }
    }
}
