using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mycelium.Core
{
    /// <summary>
    /// Lists files from a public GitHub repository using the GitHub API.
    /// Ported from Eddy3D; uses System.Text.Json instead of Newtonsoft.
    /// </summary>
    public sealed class GitHubFileLister : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly HttpClient _http;
        private readonly bool _disposeClient;

        public GitHubFileLister(string userAgent = "mycelium-file-lister-csharp")
        {
            _http = new HttpClient();
            _disposeClient = true;
            InitHeaders(_http, userAgent);
        }

        public GitHubFileLister(HttpClient httpClient, string userAgent = "mycelium-file-lister-csharp")
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _disposeClient = false;
            InitHeaders(_http, userAgent);
        }

        /// <summary>
        /// Lists all file paths ("blob" entries) in a repo at a given ref (branch/commit/tag).
        /// Works without a token for public repos.
        /// </summary>
        public async Task<IReadOnlyList<string>> ListFilesAsync(
            string owner,
            string repo,
            string gitRef = "HEAD",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Owner is required.", nameof(owner));
            if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentException("Repo is required.", nameof(repo));

            // 1) Try recursive in one request.
            var recursive = await GetTreeAsync(owner, repo, gitRef, true, cancellationToken);
            var files = recursive.Tree
                .Where(e => string.Equals(e.Type, "blob", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Path)
                .ToList();

            if (!recursive.Truncated)
                return files.OrderBy(p => p, StringComparer.Ordinal).ToList();

            // 2) Fallback: walk the tree non-recursively using SHAs to avoid truncation.
            var results = new HashSet<string>(StringComparer.Ordinal);

            var root = await GetTreeAsync(owner, repo, gitRef, false, cancellationToken);
            foreach (var e in root.Tree)
                if (IsBlob(e)) results.Add(e.Path);

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>(root.Tree.Where(IsTree).Select(e => e.Sha).Where(s => !string.IsNullOrWhiteSpace(s)));

            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sha = queue.Dequeue();
                if (!visited.Add(sha)) continue;

                var subtree = await GetTreeAsync(owner, repo, sha, false, cancellationToken);
                foreach (var e in subtree.Tree)
                {
                    if (IsBlob(e)) results.Add(e.Path);
                    else if (IsTree(e) && !string.IsNullOrWhiteSpace(e.Sha)) queue.Enqueue(e.Sha);
                }
            }

            return results.OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        public async Task<string> GetLatestCommitShaAsync(
            string owner,
            string repo,
            string gitRef = "HEAD",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Owner is required.", nameof(owner));
            if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentException("Repo is required.", nameof(repo));

            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/commits/{Uri.EscapeDataString(gitRef)}";
            using (var resp = await _http.GetAsync(url, cancellationToken))
            {
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(cancellationToken);
                var data = JsonSerializer.Deserialize<CommitResponse>(json, JsonOptions)
                           ?? throw new InvalidOperationException("Failed to parse GitHub commit response.");
                return data.Sha;
            }
        }

        public void Dispose()
        {
            if (_disposeClient) _http.Dispose();
        }

        // --- helpers ---

        private static void InitHeaders(HttpClient http, string userAgent)
        {
            if (!http.DefaultRequestHeaders.UserAgent.Any())
                http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

            if (!http.DefaultRequestHeaders.Accept.Any(h => h.MediaType == "application/vnd.github+json"))
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            if (!http.DefaultRequestHeaders.Contains("X-GitHub-Api-Version"))
                http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        private async Task<TreeResponse> GetTreeAsync(
            string owner,
            string repo,
            string idOrRef,          // branch/commit/tag OR a tree SHA
            bool recursive,
            CancellationToken ct)
        {
            var rec = recursive ? "?recursive=1" : "";
            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/git/trees/{Uri.EscapeDataString(idOrRef)}{rec}";
            using (var resp = await _http.GetAsync(url, ct))
            {
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(ct);
                var data = JsonSerializer.Deserialize<TreeResponse>(json, JsonOptions)
                           ?? throw new InvalidOperationException("Failed to parse GitHub tree response.");
                return data;
            }
        }

        private static bool IsBlob(TreeEntry e) => string.Equals(e.Type, "blob", StringComparison.OrdinalIgnoreCase);
        private static bool IsTree(TreeEntry e) => string.Equals(e.Type, "tree", StringComparison.OrdinalIgnoreCase);

        // DTOs
        private sealed class CommitResponse
        {
            public string Sha { get; set; }
        }

        private sealed class TreeResponse
        {
            public string Sha { get; set; }
            public bool Truncated { get; set; }
            public List<TreeEntry> Tree { get; set; }
        }

        private sealed class TreeEntry
        {
            public string Path { get; set; }
            public string Mode { get; set; }
            public string Type { get; set; }
            public string Sha { get; set; }
            public int? Size { get; set; }
            public string Url { get; set; }
        }
    }
}
