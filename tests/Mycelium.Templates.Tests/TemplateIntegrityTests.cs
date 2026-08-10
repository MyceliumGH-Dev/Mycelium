using System.Text;
using System.Xml.Linq;
using TemplateSync.Cli;
using Xunit;
using Xunit.Abstractions;

namespace Mycelium.Templates.Tests;

/// <summary>
/// Validates a checkout of MyceliumGH-Dev/Mycelium-Templates against the component
/// definitions in this repo's sources.
///
/// Why this exists: the Mycelium Templates component resolves its branch from the running
/// assembly version and hands the user whatever .ghx it finds. Nothing at run time checks
/// that the definition's components still exist or still have the ports the plug-in
/// registers, so a stale template fails silently on the user's canvas — the exact failure
/// that shipped on `main`, where the Massing Generator was archived under the assembly's
/// GUID rather than its own and could not be instantiated at all.
///
/// Rhino-free by construction: everything here is XML and C# source parsing, so it runs on
/// Linux CI. See <c>.github/workflows/template-integrity.yml</c>, which checks the template
/// repo out beside this one before running.
/// </summary>
public sealed class TemplateIntegrityTests
{
    private readonly ITestOutputHelper _output;

    public TemplateIntegrityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Every_template_is_well_formed_xml()
    {
        if (!TryLoadFixture(out var fixture)) return;

        foreach (var path in fixture.TemplatePaths)
        {
            // Non-null once TryLoadFixture has returned true.
            var relative = Path.GetRelativePath(fixture.RepoDir!, path);
            var exception = Record.Exception(() => XDocument.Load(path));
            Assert.True(exception == null, $"{relative} is not valid XML: {exception?.Message}");
        }
    }

    [Fact]
    public void Every_template_is_a_grasshopper_archive_containing_mycelium_objects()
    {
        if (!TryLoadFixture(out var fixture)) return;

        foreach (var (relative, usages, doc) in fixture.Templates)
        {
            Assert.True(doc.Root?.Name.LocalName == "Archive",
                $"{relative} has root element <{doc.Root?.Name.LocalName}>, expected <Archive>.");

            // A template with no Mycelium components is either misfiled or was saved from a
            // definition that lost them — either way it is not a Mycelium template.
            Assert.True(usages.Count > 0,
                $"{relative} contains no objects from the Mycelium library ({fixture.LibraryGuid}).");
        }
    }

    [Fact]
    public void Every_archived_component_guid_resolves_to_a_registered_component()
    {
        if (!TryLoadFixture(out var fixture)) return;

        var failures = new List<string>();

        foreach (var (relative, usages, _) in fixture.Templates)
        {
            foreach (var usage in usages.Where(u => !fixture.Definitions.ContainsKey(u.ComponentGuid)))
            {
                failures.Add($"{relative}: \"{usage.ComponentName}\" is archived under " +
                             $"{usage.ComponentGuid}, which no component registers. Grasshopper " +
                             "cannot instantiate it — the user gets an unresolved placeholder.");
            }
        }

        AssertNoFailures(failures,
            "Run: dotnet run --project tools/TemplateSync.Cli -- --fix");
    }

    [Fact]
    public void Archived_port_counts_match_the_component_definitions()
    {
        if (!TryLoadFixture(out var fixture)) return;

        var failures = new List<string>();

        foreach (var (relative, usages, _) in fixture.Templates)
        {
            foreach (var usage in usages)
            {
                if (!fixture.Definitions.TryGetValue(usage.ComponentGuid, out var definition))
                {
                    continue; // reported by the GUID test; not this test's business
                }

                if (usage.Inputs.Count != definition.Inputs.Count)
                {
                    failures.Add($"{relative}: {definition.Name} has {usage.Inputs.Count} archived " +
                                 $"inputs, the plug-in registers {definition.Inputs.Count}.");
                }

                if (usage.Outputs.Count != definition.Outputs.Count)
                {
                    failures.Add($"{relative}: {definition.Name} has {usage.Outputs.Count} archived " +
                                 $"outputs, the plug-in registers {definition.Outputs.Count}.");
                }
            }
        }

        // Ports cannot be added by editing XML — the template has to be opened and re-saved in
        // Grasshopper, so --fix deliberately does not offer to do it.
        AssertNoFailures(failures,
            "Open the template in Grasshopper and re-save it; --fix cannot add or remove ports.");
    }

    [Fact]
    public void Archived_port_labels_match_the_component_definitions()
    {
        if (!TryLoadFixture(out var fixture)) return;

        var failures = new List<string>();

        foreach (var (relative, usages, _) in fixture.Templates)
        {
            foreach (var usage in usages)
            {
                if (!fixture.Definitions.TryGetValue(usage.ComponentGuid, out var definition))
                {
                    continue;
                }

                Compare(relative, definition.Name, "input", usage.Inputs, definition.Inputs, failures);
                Compare(relative, definition.Name, "output", usage.Outputs, definition.Outputs, failures);
            }
        }

        AssertNoFailures(failures,
            "Run: dotnet run --project tools/TemplateSync.Cli -- --fix");
    }

    private static void Compare(
        string relative, string component, string kind,
        IReadOnlyList<ParameterSlot> archived, IReadOnlyList<ParameterDefinition> expected,
        List<string> failures)
    {
        if (archived.Count != expected.Count)
        {
            return; // count mismatch makes positional comparison meaningless
        }

        for (var i = 0; i < expected.Count; i++)
        {
            if (archived[i].Name == expected[i].Name && archived[i].NickName == expected[i].NickName)
            {
                continue;
            }

            failures.Add($"{relative}: {component} {kind} [{i}] is " +
                         $"\"{archived[i].Name}\"/\"{archived[i].NickName}\", the plug-in registers " +
                         $"\"{expected[i].Name}\"/\"{expected[i].NickName}\".");
        }
    }

    private static void AssertNoFailures(IReadOnlyList<string> failures, string remedy)
    {
        if (failures.Count == 0)
        {
            return;
        }

        var message = new StringBuilder()
            .AppendLine($"{failures.Count} template problem(s):")
            .AppendLine();

        foreach (var failure in failures)
        {
            message.Append("  ").AppendLine(failure);
        }

        message.AppendLine().Append(remedy);
        Assert.Fail(message.ToString());
    }

    /// <summary>
    /// Loads the fixture, or reports that there is nothing to check.
    ///
    /// These tests run only when asked — set MYCELIUM_TEMPLATE_REPO_DIR to a checkout, or
    /// MYCELIUM_REQUIRE_TEMPLATES=true to search the conventional locations. Otherwise they
    /// no-op, because a stale template in a different repository is not a reason for the
    /// plug-in's own `dotnet test Mycelium.sln` to fail.
    ///
    /// When the caller HAS opted in and no checkout turns up, that is a misconfigured job
    /// verifying nothing, so it fails.
    /// </summary>
    private bool TryLoadFixture(out TemplateFixture fixture)
    {
        fixture = TemplateFixture.Instance;

        if (fixture.RepoDir != null)
        {
            return true;
        }

        var required = string.Equals(
            Environment.GetEnvironmentVariable("MYCELIUM_REQUIRE_TEMPLATES"), "true",
            StringComparison.OrdinalIgnoreCase);

        Assert.False(required,
            "MYCELIUM_REQUIRE_TEMPLATES is set but no Mycelium-Templates checkout was found, so " +
            "these tests would have verified nothing. Check the repo out beside this one (see " +
            ".github/workflows/template-integrity.yml) or set MYCELIUM_TEMPLATE_REPO_DIR. " +
            $"Looked in: {string.Join(", ", TemplateFixture.SearchedPaths)}");

        _output.WriteLine("Skipped: no Mycelium-Templates checkout found. Clone it beside this " +
                          "repo or set MYCELIUM_TEMPLATE_REPO_DIR to run these tests locally.");
        return false;
    }
}
