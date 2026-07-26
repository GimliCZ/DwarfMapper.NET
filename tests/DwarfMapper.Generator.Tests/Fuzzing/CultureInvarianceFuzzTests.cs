// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.Generator.Tests.Fuzzing;

namespace DwarfMapper.Generator.Tests.Fuzzing;

/// <summary>
///     The generator must emit byte-identical code no matter what culture the build machine runs under.
///     <para>
///         <c>DeterminismFuzzTests</c> asserts the generator is stable across two runs — but both runs happen
///         in one process under one culture, so a culture-sensitive construct is stable there and still
///         produces different output on a colleague's machine. That is precisely how four comparer-less
///         <c>SortedSet&lt;(string,string)&gt;</c> survived: the emitted check, message and diagnostic order
///         went through <c>string.CompareTo</c>, which is culture-sensitive, and every existing determinism
///         test agreed with itself anyway.
///     </para>
///     <para>
///         So this varies the axis those tests hold fixed. <c>tr-TR</c> is the deliberate choice: Turkish is
///         the classic ordering/casing trap (dotted vs dotless i, so <c>"I".ToLower()</c> is not <c>"i"</c>),
///         and it reorders strings differently from invariant. <c>de-DE</c> adds a second, milder collation.
///         <c>DeterminismSourceScanTests</c> bans the constructs syntactically; this proves the ban holds
///         behaviourally, which is the half a source scan can never cover.
///     </para>
/// </summary>
public class CultureInvarianceFuzzTests
{
    /// <summary>
    ///     Runs <paramref name="body" /> with both the current and UI culture switched, restoring them
    ///     afterwards even on failure. xUnit may reuse the thread across tests, so leaking a culture here would
    ///     silently contaminate unrelated tests — a far worse failure than the one being hunted.
    /// </summary>
    private static T UnderCulture<T>(string cultureName, Func<T> body)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = new CultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Behavioral_schema_output_is_identical_under_tr_TR_and_invariant(int seed)
    {
        var src = SyntheticSchema.GenerateBehavioral(seed);

        var invariant = UnderCulture("", () => GeneratorTestHarness.Run(src).GeneratedSource);
        var turkish = UnderCulture("tr-TR", () => GeneratorTestHarness.Run(src).GeneratedSource);

        Assert.Equal(invariant, turkish);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Advanced_feature_schema_output_is_identical_under_de_DE_and_invariant(int seed)
    {
        var src = SyntheticSchema.Generate(seed);

        var invariant = UnderCulture("", () => GeneratorTestHarness.Run(src).GeneratedSource);
        var german = UnderCulture("de-DE", () => GeneratorTestHarness.Run(src).GeneratedSource);

        Assert.Equal(invariant, german);
    }

    [Fact]
    public void Ambient_validation_ordering_is_identical_across_cultures()
    {
        // Targets the exact code the culture bug lived in. The ambient validator sorts (source, destination)
        // pairs to order its emitted IsProvided checks and its DWARF061/063 diagnostics; those pairs are
        // fully-qualified type names, so a culture-sensitive sort reorders them. Member names here are chosen
        // to collide under Turkish casing/collation ('I'/'i' and '_') rather than to look pretty.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class IId { public int V { get; set; } }
            public class iid { public int V { get; set; } }
            public class Idto { public int V { get; set; } }
            public class idto { public int V { get; set; } }
            public class Consumer
            {
                public Idto A(global::DwarfMapper.IDwarfMapper m, IId d) => m.Map<Idto>(d);
                public idto B(global::DwarfMapper.IDwarfMapper m, iid d) => m.Map<idto>(d);
            }
            """;
        var root = "[assembly: global::DwarfMapper.DwarfMapperValidationRoot]\n" + src;

        var invariant = UnderCulture("", () => GeneratorTestHarness.RunAndGetSource(root, "DwarfMapper.Validate.g.cs"));
        var turkish = UnderCulture("tr-TR", () => GeneratorTestHarness.RunAndGetSource(root, "DwarfMapper.Validate.g.cs"));

        Assert.False(string.IsNullOrEmpty(invariant),
            "Expected a generated Validate method — without it this test would pass over nothing.");
        Assert.Equal(invariant, turkish);
    }

    [Fact]
    public void The_culture_switch_actually_takes_effect()
    {
        // Negative control for the harness, not the generator. If UnderCulture silently failed to switch — a
        // .NET version pinning CultureInfo, an xUnit thread quirk — every assertion above would compare
        // invariant against invariant and pass while testing nothing.
        var underTurkish = UnderCulture("tr-TR", () => "I".ToLower(CultureInfo.CurrentCulture));
        var underInvariant = UnderCulture("", () => "I".ToLower(CultureInfo.CurrentCulture));

        Assert.NotEqual(underInvariant, underTurkish);   // "i" vs "ı" — the dotless i
        Assert.Equal("i", underInvariant);
    }
}
