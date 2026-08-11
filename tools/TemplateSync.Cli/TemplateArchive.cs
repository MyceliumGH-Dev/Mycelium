using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TemplateSync.Cli;

/// <summary>One archived parameter port, kept alongside the XML it came from so it can be rewritten.</summary>
internal sealed class ParameterSlot
{
    public required XElement ItemsElement { get; init; }
    public required string Name { get; init; }
    public required string NickName { get; init; }
}

/// <summary>One Mycelium component instance found inside a .ghx definition.</summary>
internal sealed class TemplateComponentUsage
{
    public required string TemplatePath { get; init; }
    public required Guid ComponentGuid { get; init; }
    public required string ComponentName { get; init; }
    public required XElement ItemsElement { get; init; }
    public List<ParameterSlot> Inputs { get; } = new();
    public List<ParameterSlot> Outputs { get; } = new();
}

/// <summary>
/// Reads and patches the Grasshopper XML archive format (.ghx).
///
/// Archive shape, for the parts this tool touches:
///
///   Archive > chunk[DefinitionObjects] > chunks > chunk[Object]
///     items: GUID (the component's ComponentGuid), Lib (the providing assembly's id), Name
///     chunks > chunk[Container]
///       chunks > chunk[param_input] / chunk[param_output]
///         items: Name, NickName, ...
///
/// A variable-parameter component writes its ports as an explicit InputParam[i]/OutputParam[i]
/// list nested one level deeper inside a "ParameterData" chunk instead. Mycelium ships no
/// variable-parameter components today, but reading only the first shape is exactly how a
/// scan starts reporting "0 ports" for something that has plenty, so both are handled.
/// </summary>
internal static class TemplateArchive
{
    public static IEnumerable<string> EnumerateTemplates(string repoDir)
    {
        return Directory
            .EnumerateFiles(repoDir, "*.ghx", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
                                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal);
    }

    public static List<TemplateComponentUsage> ExtractUsages(XDocument doc, string templatePath, Guid libraryGuid)
    {
        var usages = new List<TemplateComponentUsage>();

        if (doc.Root?.Name.LocalName != "Archive")
        {
            return usages;
        }

        // Descendants, not a fixed path: components inside a Cluster sit deeper in the tree,
        // and a cluster's contents are exactly as version-sensitive as top-level objects.
        foreach (var objChunk in doc.Root.Descendants("chunk")
                     .Where(c => c.Attribute("name")?.Value == "Object"))
        {
            var items = objChunk.Element("items");
            if (items == null)
            {
                continue;
            }

            var lib = GetItemValue(items, "Lib");
            if (lib == null || !Guid.TryParse(lib, out var libGuid) || libGuid != libraryGuid)
            {
                continue;
            }

            if (!Guid.TryParse(GetItemValue(items, "GUID"), out var componentGuid))
            {
                continue;
            }

            var usage = new TemplateComponentUsage
            {
                TemplatePath = templatePath,
                ComponentGuid = componentGuid,
                ComponentName = GetItemValue(items, "Name") ?? "Unknown",
                ItemsElement = items,
            };

            var container = objChunk.Elements("chunks")
                .SelectMany(c => c.Elements("chunk"))
                .FirstOrDefault(c => c.Attribute("name")?.Value == "Container");

            ReadParameterSlots(container?.Element("chunks"), usage);
            usages.Add(usage);
        }

        return usages;
    }

    private static void ReadParameterSlots(XElement? parameterChunks, TemplateComponentUsage usage)
    {
        if (parameterChunks == null)
        {
            return;
        }

        Collect(parameterChunks.Elements("chunk"),
            n => n.StartsWith("param_input", StringComparison.OrdinalIgnoreCase),
            n => n.StartsWith("param_output", StringComparison.OrdinalIgnoreCase));

        if (usage.Inputs.Count > 0 || usage.Outputs.Count > 0)
        {
            return;
        }

        var explicitList = parameterChunks.Elements("chunk")
            .FirstOrDefault(c => c.Attribute("name")?.Value == "ParameterData")
            ?.Element("chunks")?.Elements("chunk");

        if (explicitList != null)
        {
            Collect(explicitList,
                n => n.Equals("InputParam", StringComparison.Ordinal),
                n => n.Equals("OutputParam", StringComparison.Ordinal));
        }

        void Collect(IEnumerable<XElement> chunks, Func<string, bool> isInput, Func<string, bool> isOutput)
        {
            foreach (var chunk in chunks)
            {
                var chunkName = chunk.Attribute("name")?.Value;
                if (chunkName == null)
                {
                    continue;
                }

                var input = isInput(chunkName);
                if (!input && !isOutput(chunkName))
                {
                    continue;
                }

                var paramItems = chunk.Element("items");
                if (paramItems == null)
                {
                    continue;
                }

                (input ? usage.Inputs : usage.Outputs).Add(new ParameterSlot
                {
                    ItemsElement = paramItems,
                    Name = GetItemValue(paramItems, "Name") ?? string.Empty,
                    NickName = GetItemValue(paramItems, "NickName") ?? string.Empty,
                });
            }
        }
    }

    /// <summary>True when the file starts with a UTF-8 byte-order mark.</summary>
    public static bool HasUtf8Bom(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[3];
        return stream.Read(head, 0, 3) == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
    }

    /// <summary>
    /// Writes the archive back, preserving whether it carried a byte-order mark.
    ///
    /// Grasshopper is inconsistent about this — templates on `main` have no BOM, the ones
    /// on the 0.1.0.4 branch do — and XDocument.Save(string) unconditionally writes one.
    /// Either direction shows up as a change to the XML declaration of every file touched:
    /// an encoding edit riding along with a semantic one, in files nobody reviews line by
    /// line. The diff should contain exactly the labels and GUIDs that changed.
    /// </summary>
    public static void Save(XDocument doc, string path, bool emitBom)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: emitBom),
            Indent = false,
            OmitXmlDeclaration = false,
        };

        using var writer = XmlWriter.Create(path, settings);
        doc.Save(writer);
    }

    public static bool SetItemValue(XElement itemsElement, string itemName, string value)
    {
        var item = FindItem(itemsElement, itemName);
        if (item == null || item.Value.Trim() == value)
        {
            return false;
        }

        item.Value = value;
        return true;
    }

    public static string? GetItemValue(XElement itemsElement, string itemName) =>
        FindItem(itemsElement, itemName)?.Value?.Trim();

    private static XElement? FindItem(XElement itemsElement, string itemName) =>
        itemsElement.Elements("item").FirstOrDefault(
            i => string.Equals(i.Attribute("name")?.Value, itemName, StringComparison.OrdinalIgnoreCase));
}
