using System.Text;
using System.Text.RegularExpressions;

namespace TemplateSync.Cli;

internal sealed record ParameterDefinition(string Name, string NickName);

internal sealed class ComponentIoDefinition
{
    public required Guid ComponentGuid { get; init; }
    public required string ClassName { get; init; }
    public required string Name { get; init; }
    public required string NickName { get; init; }
    public List<ParameterDefinition> Inputs { get; } = new();
    public List<ParameterDefinition> Outputs { get; } = new();
}

/// <summary>
/// A C# class body carved out of a source file, plus the name of the class it derives from.
/// </summary>
internal sealed record ClassBlock(string Name, string? BaseName, string Body);

/// <summary>
/// Reads component metadata straight out of the plug-in sources. Parsing rather than
/// reflecting keeps the tool Rhino-free: RhinoCommon/Grasshopper only ship usable
/// assemblies on a machine with Rhino installed, and a template check that can only run
/// there is a check that never runs in CI.
/// </summary>
internal static class ComponentDefinitions
{
    private static readonly Regex ClassDeclaration = new(
        @"\bclass\s+(?<name>\w+)\s*(?::\s*(?<base>\w+))?[^{;]*\{",
        RegexOptions.Compiled);

    private static readonly Regex ComponentGuidPattern = new(
        @"ComponentGuid\s*=>\s*new\s+Guid\(\s*""(?<guid>[0-9a-fA-F\-]{36})""\s*\)",
        RegexOptions.Compiled);

    // : base("Massing Generator", "Massing", "description", "Mycelium", "Massing")
    private static readonly Regex BaseCall = new(
        @":\s*base\(\s*""(?<name>[^""]*)""\s*,\s*""(?<nick>[^""]*)""",
        RegexOptions.Compiled);

    // pManager.AddNumberParameter("MinFloors", "Fmin", ...)
    private static readonly Regex AddParameter = new(
        @"pManager\.Add\w*Parameter\(\s*""(?<name>[^""]*)""\s*,\s*""(?<nick>[^""]*)""",
        RegexOptions.Compiled);

    // public override Guid Id => new Guid("...") inside a GH_AssemblyInfo subclass.
    private static readonly Regex AssemblyIdPattern = new(
        @"\bId\s*=>\s*new\s+Guid\(\s*""(?<guid>[0-9a-fA-F\-]{36})""\s*\)",
        RegexOptions.Compiled);

    /// <summary>
    /// The library id every Mycelium object records in its archive "Lib" item. Templates are
    /// matched on this, so a wrong value would make the whole scan silently find nothing.
    /// </summary>
    public static Guid LoadLibraryGuid(string sourceRoot)
    {
        foreach (var file in EnumerateSourceFiles(sourceRoot))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("GH_AssemblyInfo", StringComparison.Ordinal))
            {
                continue;
            }

            var match = AssemblyIdPattern.Match(text);
            if (match.Success)
            {
                return Guid.Parse(match.Groups["guid"].Value);
            }
        }

        throw new InvalidOperationException(
            $"No GH_AssemblyInfo with an Id override found under '{sourceRoot}'. " +
            "Without the library id no template object can be attributed to Mycelium.");
    }

    public static Dictionary<Guid, ComponentIoDefinition> Load(string sourceRoot)
    {
        var classes = new Dictionary<string, ClassBlock>(StringComparer.Ordinal);

        foreach (var file in EnumerateSourceFiles(sourceRoot))
        {
            foreach (var block in ParseClassBlocks(StripComments(File.ReadAllText(file))))
            {
                // First declaration wins; partial classes are not used in this codebase.
                classes.TryAdd(block.Name, block);
            }
        }

        var definitions = new Dictionary<Guid, ComponentIoDefinition>();

        foreach (var block in classes.Values)
        {
            var guidMatch = ComponentGuidPattern.Match(block.Body);
            if (!guidMatch.Success)
            {
                // Abstract bases (BuildingConfigComponent) declare no ComponentGuid; only
                // concrete components are documented and archived.
                continue;
            }

            var baseCall = BaseCall.Match(block.Body);
            if (!baseCall.Success)
            {
                continue;
            }

            var definition = new ComponentIoDefinition
            {
                ComponentGuid = Guid.Parse(guidMatch.Groups["guid"].Value),
                ClassName = block.Name,
                Name = baseCall.Groups["name"].Value,
                NickName = baseCall.Groups["nick"].Value,
            };

            // Register*Params may live on an abstract base — every *Config component inherits
            // its eight inputs from BuildingConfigComponent and declares none of its own.
            definition.Inputs.AddRange(ResolveParameters(block, classes, "RegisterInputParams"));
            definition.Outputs.AddRange(ResolveParameters(block, classes, "RegisterOutputParams"));

            definitions[definition.ComponentGuid] = definition;
        }

        return definitions;
    }

    private static IEnumerable<ParameterDefinition> ResolveParameters(
        ClassBlock block, IReadOnlyDictionary<string, ClassBlock> classes, string methodName)
    {
        var current = block;
        var guard = 0;

        while (guard++ < 16)
        {
            var body = ExtractMethodBody(current.Body, methodName);
            if (body != null)
            {
                return AddParameter.Matches(body)
                    .Select(m => new ParameterDefinition(m.Groups["name"].Value, m.Groups["nick"].Value))
                    .ToList();
            }

            if (current.BaseName == null || !classes.TryGetValue(current.BaseName, out var parent))
            {
                break;
            }

            current = parent;
        }

        return Array.Empty<ParameterDefinition>();
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var sep = Path.DirectorySeparatorChar;
            if (file.Contains($"{sep}bin{sep}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{sep}obj{sep}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return file;
        }
    }

    /// <summary>
    /// Blanks out comments, leaving string literals intact.
    ///
    /// Not cosmetic: prose containing the word "class" (as in "Base class for the per-typology
    /// config components") matches the class-declaration pattern, and because Regex.Matches
    /// resumes after the end of each match — and the bogus match runs all the way to the next
    /// '{', i.e. past the real declaration — the genuine class is then never matched at all.
    /// The symptom was every *Config component reporting that the sources register 0 ports,
    /// because the abstract base holding their shared RegisterInputParams had vanished from the
    /// class table.
    /// </summary>
    internal static string StripComments(string source)
    {
        var sb = new StringBuilder(source.Length);
        var i = 0;

        while (i < source.Length)
        {
            var c = source[i];

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                var newline = source.IndexOf('\n', i);
                if (newline < 0) break;
                sb.Append('\n');           // keep line structure for readable diagnostics
                i = newline + 1;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) break;
                sb.Append(' ');
                i = end + 2;
                continue;
            }

            if (c is '"' or '\'')
            {
                var close = SkipLiteral(source, i);
                if (close < 0)
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                sb.Append(source, i, close - i + 1);
                i = close + 1;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Carves each class body out by brace matching from the opening brace of its declaration.
    /// Nested types would be re-reported as their own blocks, which is harmless: they carry no
    /// ComponentGuid.
    /// </summary>
    internal static IEnumerable<ClassBlock> ParseClassBlocks(string source)
    {
        foreach (Match declaration in ClassDeclaration.Matches(source))
        {
            var openBrace = declaration.Index + declaration.Length - 1;
            var end = FindMatchingBrace(source, openBrace);
            if (end < 0)
            {
                continue;
            }

            var baseName = declaration.Groups["base"].Success ? declaration.Groups["base"].Value : null;
            yield return new ClassBlock(
                declaration.Groups["name"].Value,
                baseName,
                source.Substring(openBrace, end - openBrace + 1));
        }
    }

    private static string? ExtractMethodBody(string classBody, string methodName)
    {
        var signature = classBody.IndexOf(methodName, StringComparison.Ordinal);
        if (signature < 0)
        {
            return null;
        }

        var openBrace = classBody.IndexOf('{', signature);
        if (openBrace < 0)
        {
            return null;
        }

        var end = FindMatchingBrace(classBody, openBrace);
        return end < 0 ? null : classBody.Substring(openBrace, end - openBrace + 1);
    }

    /// <summary>
    /// Brace matching that ignores braces inside string/char literals and comments — a
    /// description string containing '{' would otherwise truncate the body and silently drop
    /// every parameter registered after it.
    /// </summary>
    private static int FindMatchingBrace(string text, int openBraceIndex)
    {
        var depth = 0;

        for (var i = openBraceIndex; i < text.Length; i++)
        {
            var c = text[i];

            switch (c)
            {
                case '/' when i + 1 < text.Length && text[i + 1] == '/':
                    i = text.IndexOf('\n', i);
                    if (i < 0) return -1;
                    continue;

                case '/' when i + 1 < text.Length && text[i + 1] == '*':
                    i = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (i < 0) return -1;
                    i++;
                    continue;

                case '"':
                case '\'':
                    i = SkipLiteral(text, i);
                    if (i < 0) return -1;
                    continue;

                case '{':
                    depth++;
                    break;

                case '}':
                    depth--;
                    if (depth == 0) return i;
                    break;
            }
        }

        return -1;
    }

    /// <summary>Returns the index of the literal's closing quote, or -1 if unterminated.</summary>
    private static int SkipLiteral(string text, int start)
    {
        var quote = text[start];

        // Verbatim string: @"..." where "" is an escaped quote and backslash means nothing.
        var verbatim = quote == '"' && start > 0 && text[start - 1] == '@';

        for (var i = start + 1; i < text.Length; i++)
        {
            if (verbatim)
            {
                if (text[i] != quote) continue;
                if (i + 1 < text.Length && text[i + 1] == quote) { i++; continue; }
                return i;
            }

            if (text[i] == '\\') { i++; continue; }
            if (text[i] == quote) return i;
            if (text[i] == '\n') return -1;
        }

        return -1;
    }
}
