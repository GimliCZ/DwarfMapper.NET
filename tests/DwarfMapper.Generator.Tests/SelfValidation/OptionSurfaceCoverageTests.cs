// SPDX-License-Identifier: GPL-2.0-only

using System.IO;
using DwarfMapper;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Every shipping option must be DEMONSTRATED somewhere a reader can run, not merely documented.
/// </summary>
/// <remarks>
///     <para>
///         Round 18 measured the gap: six of fifteen <c>[DwarfMapper]</c> options — including
///         <c>SkipNullSourceMembers</c>, the one a migrating consumer reaches for first — appeared in
///         <b>no</b> sample file at all. They were fully documented in <c>options.md</c> and demonstrated
///         nowhere, which is exactly how a present feature gets asked for repeatedly.
///     </para>
///     <para>
///         Nothing in the repo failed while that was true. This is the gate that makes it fail. It follows the
///         house rule the other self-audits learned the hard way: reflect over the generator's own taxonomy
///         rather than maintaining a hand-written list, because a hand list is precisely what drifts.
///     </para>
///     <para>
///         <b>Scope, stated honestly:</b> this covers the option SURFACE. It says nothing about the
///         multi-assembly, DI, ambient-registry SHAPE that actually let Round 18's defects through — that
///         needs a consumer-shaped project, and no reflection over an attribute can substitute for it.
///     </para>
/// </remarks>
public class OptionSurfaceCoverageTests
{
    /// <summary>
    ///     Options deliberately not demonstrated, each with the reason. Deliberately tiny: an allowlist that
    ///     grows is the failure mode this test exists to prevent, so adding to it should feel expensive.
    /// </summary>
    private static readonly Dictionary<string, string> NotDemonstrable = new(StringComparer.Ordinal)
    {
        ["MaxDepth"] =
            "demonstrating it means building a graph deeper than the bound and catching "
            + "DwarfMappingDepthException — covered by the cycle/depth runtime suites, where the assertion "
            + "belongs; a sample that throws on purpose reads as a broken sample",

        ["GenerateExtensions"] =
            "its effect is the ABSENCE of generated extension methods, which a runnable sample cannot show "
            + "— asserted structurally in the generator tests instead",

        ["ImplicitConversions"] =
            "true is the default and changes nothing to observe; false turns DWARF038 into a BUILD ERROR, so "
            + "a sample demonstrating the difference could not compile. Covered by the generator tests, which "
            + "can assert a diagnostic without shipping a broken sample"
    };

    /// <summary>Files a reader can run: the conformance app and the gallery.</summary>
    private static IEnumerable<string> SampleSources()
    {
        var samples = Path.Combine(RepoRoot(), "samples");

        return Directory.EnumerateFiles(samples, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal));
    }

    [Fact]
    public void Every_class_level_option_is_demonstrated_in_a_runnable_sample()
    {
        var sampleText = string.Concat(SampleSources().Select(File.ReadAllText));

        var options = typeof(DwarfMapperAttribute).GetProperties()
            .Where(p => p.CanWrite)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(options.Count >= 15,
            $"Only {options.Count} settable options found on [DwarfMapper]. If the surface really shrank, "
            + "lower this floor deliberately; otherwise the reflection stopped seeing the options and this "
            + "gate has gone vacuous.");

        var missing = options
            .Where(o => !NotDemonstrable.ContainsKey(o))
            .Where(o => !sampleText.Contains(o, StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            "Option(s) documented but demonstrated in NO runnable sample:\n  "
            + string.Join("\n  ", missing)
            + "\n\nAdd a Conformance feature (samples/DwarfMapper.Conformance) asserting the option's "
            + "observable runtime difference, or a Gallery example if it deserves prose — then, only if it "
            + "genuinely cannot be shown at runtime, add it to NotDemonstrable with a reason.\n\n"
            + "Six options sat in exactly this state before Round 18, including the one migrating consumers "
            + "asked for most.");
    }

    [Fact]
    public void Every_public_pair_scoped_and_member_attribute_is_demonstrated()
    {
        // The attribute surface has the same failure mode as the option surface, and [MapConstructor] was in
        // it: fully documented, exercised in no sample. F23 covered [DwarfMapperConstructor], a DIFFERENT
        // attribute, which is how the gap survived a reading of the feature list.
        var sampleText = string.Concat(SampleSources().Select(File.ReadAllText));

        var attributes = typeof(DwarfMapperAttribute).Assembly.GetTypes()
            .Where(t => t.IsPublic && typeof(Attribute).IsAssignableFrom(t))
            .Select(t => Usage(t.Name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var missing = attributes
            .Where(a => !AttributeNotDemonstrable.ContainsKey(a))
            .Where(a => !sampleText.Contains("[" + a, StringComparison.Ordinal)
                        && !sampleText.Contains("assembly: " + a, StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            "Public attribute(s) demonstrated in NO runnable sample:\n  "
            + string.Join("\n  ", missing)
            + "\n\nAdd a Conformance feature or Gallery example, or add it to AttributeNotDemonstrable with "
            + "a reason.");
    }

    /// <summary>Attributes with no place in a runnable sample, each with the reason.</summary>
    private static readonly Dictionary<string, string> AttributeNotDemonstrable = new(StringComparer.Ordinal)
    {
        ["DwarfProvidesMap"] =
            "emitted BY the generator onto the assembly as a manifest; never hand-written",

        ["DwarfRequiresMap"] =
            "emitted BY the generator as the consumption side of the same manifest",

        ["DwarfMapperValidationRoot"] =
            "marks a composition root, and its effect is a BUILD error (DWARF061) at the root — a passing "
            + "sample cannot contain the failure it guards against",

        ["DwarfMapperOptions"] =
            "assembly-level emission switch (PublicExtensions); its effect is the accessibility of generated "
            + "code, not runtime behaviour a sample can assert",

        ["UsesMap"] =
            "declares a CONSUMED ambient pair for the DWARF061 manifest when the call site cannot be "
            + "auto-detected. Its effect is on cross-assembly build-time validation, and the conformance "
            + "sample is a single assembly that validates nothing — the consumer-shaped project (R18-21) is "
            + "where this belongs"
    };

    /// <summary>Attribute usage name: strip the <c>Attribute</c> suffix and any generic arity marker.</summary>
    private static string Usage(string typeName)
    {
        var tick = typeName.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0) typeName = typeName[..tick];

        return typeName.EndsWith("Attribute", StringComparison.Ordinal)
            ? typeName[..^"Attribute".Length]
            : typeName;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "DwarfMapper.NET.sln")))
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));

        Assert.NotNull(dir);
        return dir!;
    }
}
