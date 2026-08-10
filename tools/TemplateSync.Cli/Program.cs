using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TemplateSync.Cli;

internal enum FindingKind
{
    UnresolvableComponent,
    PortCountMismatch,
    LabelDrift,
}

internal sealed record Finding(FindingKind Kind, string TemplatePath, string Component, string Message, bool Fixed);

internal static class Program
{
    private const string TemplateRepoUrl = "https://github.com/MyceliumGH-Dev/Mycelium-Templates.git";
    private const string RepoDirEnvVar = "MYCELIUM_TEMPLATE_REPO_DIR";

    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        var fix = args.Contains("--fix");
        var noGitUpdate = args.Contains("--no-git-update");
        var repoDir = ResolveRepoDir(GetOption(args, "--repo-dir"));
        var pluginRoot = FindPluginRoot();
        var sourceRoot = Path.Combine(pluginRoot, "src", "Mycelium");
        var branch = GetOption(args, "--branch") ?? ReadManifestVersion(pluginRoot);

        Console.WriteLine($"Plugin sources : {sourceRoot}");
        Console.WriteLine($"Template repo  : {repoDir}");
        Console.WriteLine($"Target branch  : {branch}");
        Console.WriteLine();

        PrepareRepo(repoDir, branch, noGitUpdate);

        var libraryGuid = ComponentDefinitions.LoadLibraryGuid(sourceRoot);
        var definitions = ComponentDefinitions.Load(sourceRoot);
        if (definitions.Count == 0)
        {
            throw new InvalidOperationException(
                $"No components parsed from '{sourceRoot}'. Refusing to report every template " +
                "object as unresolvable on the strength of a parse that clearly failed.");
        }

        Console.WriteLine($"Library id     : {libraryGuid}");
        Console.WriteLine($"Components     : {definitions.Count}");
        foreach (var d in definitions.Values.OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {d.Name,-24} {d.Inputs.Count} in / {d.Outputs.Count} out  ({d.ComponentGuid})");
        }
        Console.WriteLine();

        var byName = definitions.Values
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);

        var findings = new List<Finding>();
        var templates = TemplateArchive.EnumerateTemplates(repoDir).ToList();
        if (templates.Count == 0)
        {
            Console.WriteLine($"No .ghx templates found under {repoDir}.");
        }

        foreach (var templatePath in templates)
        {
            var relative = Path.GetRelativePath(repoDir, templatePath);
            var doc = XDocument.Load(templatePath, LoadOptions.PreserveWhitespace);
            var usages = TemplateArchive.ExtractUsages(doc, relative, libraryGuid);
            var dirty = false;

            foreach (var usage in usages)
            {
                dirty |= Inspect(usage, definitions, byName, fix, findings);
            }

            Console.WriteLine($"{relative,-42} {usages.Count} Mycelium object(s)");

            if (dirty)
            {
                doc.Save(templatePath, SaveOptions.DisableFormatting);
                Console.WriteLine($"{"",-42} -> rewritten");
            }
        }

        return Report(findings, fix);
    }

    private static bool Inspect(
        TemplateComponentUsage usage,
        IReadOnlyDictionary<Guid, ComponentIoDefinition> definitions,
        IReadOnlyDictionary<string, ComponentIoDefinition> byName,
        bool fix,
        List<Finding> findings)
    {
        var dirty = false;

        if (!definitions.TryGetValue(usage.ComponentGuid, out var definition))
        {
            // The GUID does not belong to any component this plug-in registers, so Grasshopper
            // cannot instantiate it — the object loads as an unresolved placeholder. Repair is
            // only safe when the archived display name pins down exactly one component.
            var repaired = false;
            if (fix && byName.TryGetValue(usage.ComponentName, out var candidate))
            {
                repaired = TemplateArchive.SetItemValue(
                    usage.ItemsElement, "GUID", candidate.ComponentGuid.ToString());
                dirty |= repaired;
                definition = candidate;
            }

            findings.Add(new Finding(
                FindingKind.UnresolvableComponent, usage.TemplatePath, usage.ComponentName,
                $"GUID {usage.ComponentGuid} matches no component in the current sources" +
                (repaired ? $" — rewritten to {definition!.ComponentGuid}" : ""),
                repaired));

            if (!repaired)
            {
                return dirty;
            }
        }

        dirty |= CompareSlots(usage, usage.Inputs, definition!.Inputs, "input", fix, findings);
        dirty |= CompareSlots(usage, usage.Outputs, definition.Outputs, "output", fix, findings);

        return dirty;
    }

    private static bool CompareSlots(
        TemplateComponentUsage usage,
        IReadOnlyList<ParameterSlot> archived,
        IReadOnlyList<ParameterDefinition> expected,
        string kind,
        bool fix,
        List<Finding> findings)
    {
        if (archived.Count != expected.Count)
        {
            // Ports are positional, so once the counts differ an index-wise label comparison
            // reports noise rather than drift. Adding or removing ports safely needs a
            // reference instance to clone from; that is a re-save in Grasshopper, not a
            // text edit, so this is reported and left alone.
            findings.Add(new Finding(
                FindingKind.PortCountMismatch, usage.TemplatePath, usage.ComponentName,
                $"{kind} count is {archived.Count}, sources register {expected.Count} " +
                "— open and re-save the template in Grasshopper",
                false));
            return false;
        }

        var dirty = false;

        for (var i = 0; i < expected.Count; i++)
        {
            var slot = archived[i];
            var want = expected[i];

            if (slot.Name == want.Name && slot.NickName == want.NickName)
            {
                continue;
            }

            var repaired = false;
            if (fix)
            {
                repaired = TemplateArchive.SetItemValue(slot.ItemsElement, "Name", want.Name);
                repaired |= TemplateArchive.SetItemValue(slot.ItemsElement, "NickName", want.NickName);
                dirty |= repaired;
            }

            findings.Add(new Finding(
                FindingKind.LabelDrift, usage.TemplatePath, usage.ComponentName,
                $"{kind} [{i}] is \"{slot.Name}\"/\"{slot.NickName}\", " +
                $"sources register \"{want.Name}\"/\"{want.NickName}\"",
                repaired));
        }

        return dirty;
    }

    private static int Report(IReadOnlyList<Finding> findings, bool fix)
    {
        Console.WriteLine();

        if (findings.Count == 0)
        {
            Console.WriteLine("No drift: every template matches the component definitions.");
            return 0;
        }

        var unfixed = findings.Count(f => !f.Fixed);

        foreach (var group in findings.GroupBy(f => f.TemplatePath).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine(group.Key);
            foreach (var finding in group)
            {
                var marker = finding.Fixed ? "fixed " : "DRIFT ";
                Console.WriteLine($"  {marker} {finding.Component}: {finding.Message}");
            }
            Console.WriteLine();
        }

        Console.WriteLine($"{findings.Count} finding(s), {findings.Count - unfixed} fixed, {unfixed} outstanding.");

        if (!fix && findings.Count > 0)
        {
            Console.WriteLine("Re-run with --fix to rewrite what can be rewritten in place.");
        }

        // Exit non-zero only for what is still wrong, so --fix runs can gate CI too.
        return unfixed > 0 ? 1 : 0;
    }

    // --- environment -------------------------------------------------------------------

    private static string? GetOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string ResolveRepoDir(string? explicitDir)
    {
        var dir = explicitDir
                  ?? Environment.GetEnvironmentVariable(RepoDirEnvVar)
                  ?? Path.Combine(
                      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                      "Documents", "GitHub", "MyceliumGH-Dev", "Mycelium-Templates");

        return Path.GetFullPath(dir);
    }

    /// <summary>Walks up from the executable until it finds the repo root (the one with manifest.yml).</summary>
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

    private static string ReadManifestVersion(string pluginRoot)
    {
        var manifest = Path.Combine(pluginRoot, "manifest.yml");
        var match = Regex.Match(File.ReadAllText(manifest), @"^version:\s*(?<v>\S+)\s*$", RegexOptions.Multiline);
        if (!match.Success)
        {
            throw new InvalidOperationException($"No 'version:' line in {manifest}.");
        }

        return match.Groups["v"].Value;
    }

    private static void PrepareRepo(string repoDir, string branch, bool noGitUpdate)
    {
        if (!Directory.Exists(repoDir))
        {
            Console.WriteLine($"Cloning {TemplateRepoUrl} into {repoDir}");
            Directory.CreateDirectory(Path.GetDirectoryName(repoDir)!);
            Git(Path.GetDirectoryName(repoDir)!, "clone", TemplateRepoUrl, repoDir);
        }

        if (noGitUpdate)
        {
            Console.WriteLine("Skipping git update (--no-git-update).");
            return;
        }

        Git(repoDir, "fetch", "--all", "--prune");

        // A version branch that does not exist yet is the normal state before a release, and
        // is the very thing template-branch-sync.yml creates. Fall back rather than fail.
        if (Git(repoDir, false, "rev-parse", "--verify", $"origin/{branch}") == 0)
        {
            Git(repoDir, "checkout", "-B", branch, $"origin/{branch}");
        }
        else
        {
            Console.WriteLine($"warning: origin/{branch} does not exist — checking the default branch instead. " +
                              "This is what users of this version see too, since the Templates component " +
                              "falls back to main.");
            Git(repoDir, "checkout", "main");
            Git(repoDir, "pull", "--ff-only");
        }
    }

    private static void Git(string workingDir, params string[] args)
    {
        if (Git(workingDir, true, args) != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed in {workingDir}.");
        }
    }

    private static int Git(string workingDir, bool echo, params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException("Could not start git — is it on PATH?");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (echo && process.ExitCode != 0)
        {
            Console.Error.WriteLine(stderr.Trim());
        }
        else if (echo && stdout.Trim().Length > 0)
        {
            Console.WriteLine(stdout.Trim());
        }

        return process.ExitCode;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            TemplateSync.Cli — compare Mycelium-Templates against the component definitions.

            Usage:
              dotnet run --project tools/TemplateSync.Cli -- [options]

            Options:
              --repo-dir <path>   Local Mycelium-Templates checkout. Cloned if missing.
                                  Default: $MYCELIUM_TEMPLATE_REPO_DIR, else
                                  ~/Documents/GitHub/MyceliumGH-Dev/Mycelium-Templates
              --branch <name>     Branch to check. Default: the version in manifest.yml.
              --no-git-update     Do not fetch/checkout; use the working tree as it is.
              --fix               Rewrite what can be rewritten in place (port labels, and
                                  component GUIDs when the archived name is unambiguous).
              -h, --help          This text.

            Exit codes: 0 clean, 1 outstanding findings, 2 error.
            """);
    }
}
