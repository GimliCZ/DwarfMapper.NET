// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     <b>B31 / DWARF098.</b> <c>[DwarfMapperConstructor]</c> on a constructor the selector cannot use was
///     ignored at every endpoint that reads it, without a word. The EMISSION is not the defect and is
///     unchanged: <c>ConstructorSelector.IsUsableCandidate</c> filters the annotated constructor and selection
///     falls back to the safe default policy, which is right — selecting it would emit code the compiler
///     rejects. What was missing is the REPORT. A caller who marks a <c>private</c> constructor and gets
///     object-initializer mapping had no way to learn why; measured at A14, the output was byte-identical to
///     the unannotated baseline.
///     <para>
///         <b>A Warning, and that answers the row's two-messages question.</b> The behaviour is defensible and
///         is kept, so this reports rather than refuses. An ABSENT annotation stays silent: nothing was
///         written, so nothing was discarded, and only the written-and-declined case is a caller mistake.
///     </para>
///     <para>
///         <b>The reason is per-filter, and the remedy is per-reason.</b> A message that named the wrong
///         remedy would be worse than silence, and this fix nearly shipped one: the first draft told every
///         inaccessible constructor to set <c>[DwarfMapper(AllowNonPublic = true)]</c>, and the probe showed a
///         <c>private</c> constructor is STILL filtered with that option set — the option widens the filter
///         only as far as the consumer's own assembly can see, which a private member of another type never
///         is. Both accessibility cases are pinned below, in both directions.
///     </para>
/// </summary>
public class AnnotatedConstructorUnusableTests
{
    private static string Source(string ctor, string options = "",
        string endpoint = "public partial Dst Map(Src s);") => $$"""
        using System.Linq;
        using DwarfMapper;
        namespace Demo;
        public class Src { public int A { get; set; } }
        public class Dst
        {
            public Dst() { }
            {{ctor}}
            public int A { get; set; }
        }
        [DwarfMapper({{options}})]
        public partial class M { {{endpoint}} }
        """;

    private static string Message(IEnumerable<Diagnostic> diags) =>
        string.Join(" | ", diags.Where(d => d.Id == "DWARF098")
            .Select(d => d.GetMessage(CultureInfo.InvariantCulture)));

    /// <summary>Every filter <c>IsUsableCandidate</c> applies, with the words its message must carry.</summary>
    public static TheoryData<string, string, string> UnusableCtors() => new()
    {
        { "private", "[DwarfMapperConstructor] private Dst(int a) { A = a; }", "it is private" },
        {
            "obsolete", "[DwarfMapperConstructor] [System.Obsolete] public Dst(int a) { A = a; }",
            "marked [Obsolete]"
        },
        { "ref parameter", "[DwarfMapperConstructor] public Dst(ref int a) { A = a; }", "passed by ref" },
        {
            "out parameter", "[DwarfMapperConstructor] public Dst(out int a) { a = 1; A = 1; }",
            "passed by out"
        },
        {
            "copy constructor", "[DwarfMapperConstructor] public Dst(Dst other) { A = other.A; }",
            "COPY constructor"
        },
    };

    [Theory]
    [MemberData(nameof(UnusableCtors))]
    public void An_unusable_annotated_constructor_is_reported_with_the_reason_that_rejected_it(
        string label, string ctor, string expectedReason)
    {
        ArgumentNullException.ThrowIfNull(ctor);
        var source = Source(ctor);
        var (diags, generated) = GeneratorTestHarness.Run(source);

        Assert.True(diags.Count(d => d.Id == "DWARF098") == 1,
            $"[{label}] expected exactly one DWARF098; got "
            + $"{diags.Count(d => d.Id == "DWARF098")}. B31: this was silent at every endpoint.");
        Assert.Contains(expectedReason, Message(diags), StringComparison.Ordinal);
        Assert.Equal(DiagnosticSeverity.Warning, diags.First(d => d.Id == "DWARF098").Severity);

        // The EMISSION is unchanged and must stay so — the fallback is the safe answer, and the fix is a
        // report, not a behaviour change. Byte-identical to the same type with no annotation at all.
        var unannotated = GeneratorTestHarness.Run(Source(ctor.Replace("[DwarfMapperConstructor] ", "",
            StringComparison.Ordinal))).GeneratedSource;
        Assert.Equal(unannotated, generated);
        GeneratorAssert.EmitsCompilableCode(source);
    }

    /// <summary>
    ///     The CONTROLS, which are the half that keeps this from being noise: a usable annotated constructor
    ///     is used and says nothing, and no annotation at all says nothing either. Only a directive that was
    ///     WRITTEN can be discarded.
    /// </summary>
    [Theory]
    [InlineData("[DwarfMapperConstructor] public Dst(int a) { A = a; }", "a usable annotated constructor")]
    [InlineData("public Dst(int a) { A = a; }", "no annotation at all")]
    public void Nothing_is_reported_when_no_directive_was_discarded(string ctor, string label)
    {
        var (diags, _) = GeneratorTestHarness.Run(Source(ctor));
        Assert.True(diags.All(d => d.Id != "DWARF098"),
            $"DWARF098 fired for {label}: {Message(diags)}");
    }

    /// <summary>
    ///     The two accessibility remedies, and the reason they cannot be one sentence.
    ///     <c>AllowNonPublic</c> widens the candidate filter to what the CONSUMER'S ASSEMBLY can reach, so it
    ///     rescues an <c>internal</c> constructor and can never rescue a <c>private</c> one. Both directions
    ///     are measured here, because "set the option" is advice that sends the private case in a circle.
    /// </summary>
    [Fact]
    public void The_accessibility_remedy_matches_what_the_option_can_actually_reach()
    {
        const string privateCtor = "[DwarfMapperConstructor] private Dst(int a) { A = a; }";
        const string internalCtor = "[DwarfMapperConstructor] internal Dst(int a) { A = a; }";

        // internal, option OFF → reported, and setting the option IS the remedy.
        var (internalOff, _) = GeneratorTestHarness.Run(Source(internalCtor));
        Assert.Contains("AllowNonPublic = true)]. Set that option", diagFor(internalOff),
            StringComparison.Ordinal);

        // internal, option ON → the constructor is USED, and there is nothing to report.
        var (internalOn, internalOnGen) = GeneratorTestHarness.Run(Source(internalCtor, "AllowNonPublic = true"));
        Assert.DoesNotContain(internalOn, d => d.Id == "DWARF098");
        Assert.Contains("new global::Demo.Dst(", internalOnGen, StringComparison.Ordinal);

        // private, option ON → STILL filtered, so the message must NOT tell the caller to set the option.
        var (privateOn, _) = GeneratorTestHarness.Run(Source(privateCtor, "AllowNonPublic = true"));
        var msg = diagFor(privateOn);
        Assert.Contains("no mapper option can reach", msg, StringComparison.Ordinal);
        Assert.Contains("Make the constructor internal", msg, StringComparison.Ordinal);

        string diagFor(IEnumerable<Diagnostic> d) => Message(d);
    }

    /// <summary>
    ///     Reported at EVERY endpoint that reads the directive, because the silence was at every endpoint.
    ///     A14 measured it at <c>CreateMap</c> and <c>Projection</c>; both are asked here, separately and
    ///     together, so a report wired into one selection path and not the other cannot pass.
    /// </summary>
    [Theory]
    [InlineData("public partial Dst Map(Src s);", 1)]
    [InlineData("public partial IQueryable<Dst> Project(IQueryable<Src> q);", 1)]
    [InlineData("public partial Dst Map(Src s); public partial IQueryable<Dst> Project(IQueryable<Src> q);", 2)]
    public void It_is_reported_once_per_mapping_method_that_selects_the_destination(
        string endpoint, int expected)
    {
        var (diags, _) = GeneratorTestHarness.Run(
            Source("[DwarfMapperConstructor] private Dst(int a) { A = a; }", endpoint: endpoint));

        Assert.Equal(expected, diags.Count(d => d.Id == "DWARF098"));
    }
}
