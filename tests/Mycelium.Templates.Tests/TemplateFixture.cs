using System.Xml.Linq;
using TemplateSync.Cli;

namespace Mycelium.Templates.Tests;

/// <summary>
/// Parses the plug-in sources and the template checkout once for the whole test class —
/// every template is a few hundred KB of XML and the source scan walks the whole tree, so
/// doing it per test would dominate the run.
/// </summary>
internal sealed class TemplateFixture
{
    private static readonly Lazy<TemplateFixture> Lazy = new(Create, isThreadSafe: true);
    private static readonly List<string> Searched = new();

    public static TemplateFixture Instance => Lazy.Value;
    public static IReadOnlyList<string> SearchedPaths => Searched;

    /// <summary>Null when no template checkout could be found.</summary>
    public string? RepoDir { get; private init; }

    public Guid LibraryGuid { get; private init; }
    public IReadOnlyDictionary<Guid, ComponentIoDefinition> Definitions { get; private init; } =
        new Dictionary<Guid, ComponentIoDefinition>();

    public IReadOnlyList<string> TemplatePaths { get; private init; } = Array.Empty<string>();

    public IReadOnlyList<(string Relative, IReadOnlyList<TemplateComponentUsage> Usages, XDocument Document)>
        Templates { get; private init; } =
        Array.Empty<(string, IReadOnlyList<TemplateComponentUsage>, XDocument)>();

    private static TemplateFixture Create()
    {
        var pluginRoot = FindPluginRoot();
        var sourceRoot = Path.Combine(pluginRoot, "src", "Mycelium");

        var libraryGuid = ComponentDefinitions.LoadLibraryGuid(sourceRoot);
        var definitions = ComponentDefinitions.Load(sourceRoot);

        if (definitions.Count == 0)
        {
            throw new InvalidOperationException(
                $"No components parsed from '{sourceRoot}'. Every template would be reported as " +
                "broken on the strength of a parse that clearly failed.");
        }

        var repoDir = FindTemplateRepo(pluginRoot);
        if (repoDir == null)
        {
            return new TemplateFixture { RepoDir = null, LibraryGuid = libraryGuid, Definitions = definitions };
        }

        var paths = TemplateArchive.EnumerateTemplates(repoDir).ToList();
        var templates = new List<(string, IReadOnlyList<TemplateComponentUsage>, XDocument)>();

        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(repoDir, path);
            XDocument doc;
            try
            {
                doc = XDocument.Load(path);
            }
            catch
            {
                continue; // the well-formed-XML test reports this; do not mask it with a crash here
            }

            templates.Add((relative, TemplateArchive.ExtractUsages(doc, relative, libraryGuid), doc));
        }

        return new TemplateFixture
        {
            RepoDir = repoDir,
            LibraryGuid = libraryGuid,
            Definitions = definitions,
            TemplatePaths = paths,
            Templates = templates,
        };
    }

    private static string FindPluginRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "manifest.yml"))
                && Directory.Exists(Path.Combine(dir.FullName, "src", "Mycelium")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the Mycelium repo root (a directory with manifest.yml and src/Mycelium) " +
            $"above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    /// Locates the template checkout, but only when the caller has opted in.
    ///
    /// Opting in matters: these tests validate a DIFFERENT repository's content, and the
    /// plug-in's own `dotnet test Mycelium.sln` should not go red because a template in
    /// Mycelium-Templates is stale. Auto-discovering a sibling checkout did exactly that —
    /// green in CI, red on the maintainer's machine, for reasons unrelated to the code under
    /// test. So: run when asked, against the repo named, and skip otherwise.
    /// </summary>
    private static string? FindTemplateRepo(string pluginRoot)
    {
        // Explicit path always wins and always opts in.
        var fromEnv = Environment.GetEnvironmentVariable("MYCELIUM_TEMPLATE_REPO_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            var explicitPath = Path.GetFullPath(fromEnv);
            Searched.Add(explicitPath);
            return Directory.Exists(explicitPath) ? explicitPath : null;
        }

        var required = string.Equals(
            Environment.GetEnvironmentVariable("MYCELIUM_REQUIRE_TEMPLATES"), "true",
            StringComparison.OrdinalIgnoreCase);

        if (!required)
        {
            return null;
        }

        // Opted in without naming a path: fall back to the conventional locations.
        var candidates = new List<string>();

        var parent = Directory.GetParent(pluginRoot)?.FullName;
        if (parent != null)
        {
            candidates.Add(Path.Combine(parent, "Mycelium-Templates"));
        }

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents", "GitHub", "MyceliumGH-Dev", "Mycelium-Templates"));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var full = Path.GetFullPath(candidate);
            Searched.Add(full);

            if (Directory.Exists(full))
            {
                return full;
            }
        }

        return null;
    }
}
