// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// The Turkish-I trap, applied to MEMBER MATCHING at generation time.
/// <para>
/// Under <c>tr-TR</c>, <c>"TITLE".ToLower()</c> is <c>"tıtle"</c> (dotless ı), not <c>"title"</c>, and
/// <c>"i".ToUpper()</c> is <c>"İ"</c> (dotted capital). So a case-insensitive match written with the AMBIENT
/// culture — <c>CurrentCultureIgnoreCase</c>, or <c>name.ToLower()</c> — silently fails to pair <c>Title</c>
/// with <c>TITLE</c> the moment the build runs on a Turkish developer's machine. The member goes unmapped,
/// mapping behaviour changes with the locale, and nothing says so.
/// </para>
/// <para>
/// The generator uses <c>OrdinalIgnoreCase</c> for case-insensitive matching and <c>ToLowerInvariant</c> for
/// <see cref="NameConvention.Flexible" /> normalisation — both culture-independent by construction, so it is
/// safe. But "safe by construction" drifts: a future edit to <c>CurrentCultureIgnoreCase</c> or <c>ToLower()</c>
/// would reintroduce the trap, and no other test would notice because they all run under an invariant/en-US
/// culture. This runs the generator under <c>tr-TR</c> and asserts the match still holds.
/// </para>
/// <para>
/// The generator is invoked on a dedicated thread with the culture pinned, so the change cannot leak into any
/// parallel test.
/// </para>
/// </summary>
public class TurkishCultureMatchingTests
{
    private static (ImmutableArray<Diagnostic> Diagnostics, string Generated) RunUnderCulture(
        string cultureName, string source)
    {
        (ImmutableArray<Diagnostic> Diagnostics, string Generated) result = default;
        var thread = new Thread(() =>
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            result = GeneratorTestHarness.Run(source);
        });
        thread.Start();
        thread.Join();
        return result;
    }

    // 'Title' vs 'TITLE' — the pair turns entirely on folding 'i'/'I', which is exactly what tr-TR breaks under
    // an ambient-culture comparison.
    private const string CaseInsensitiveSource = """
        using DwarfMapper;
        namespace Demo;
        public class Src { public string Title { get; set; } = ""; }
        public class Dst { public string TITLE { get; set; } = ""; }
        [DwarfMapper(CaseInsensitive = true)]
        public partial class M { public partial Dst Map(Src s); }
        """;

    // Flexible normalisation across casing styles, again on an 'I'-bearing name: 'TitleId' <-> 'title_id'.
    private const string FlexibleSource = """
        using DwarfMapper;
        namespace Demo;
        public class Src { public string TitleId { get; set; } = ""; }
        public class Dst { public string title_id { get; set; } = ""; }
        [DwarfMapper(NameConvention = NameConvention.Flexible)]
        public partial class M { public partial Dst Map(Src s); }
        """;

    [Fact]
    public void Case_insensitive_matching_still_pairs_I_members_under_tr_TR()
    {
        var (diagnostics, generated) = RunUnderCulture("tr-TR", CaseInsensitiveSource);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF001"); // TITLE must not be left unmapped
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("TITLE = s.Title", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Flexible_matching_still_pairs_I_members_under_tr_TR()
    {
        var (diagnostics, generated) = RunUnderCulture("tr-TR", FlexibleSource);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF001");
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("title_id = s.TitleId", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void The_result_is_identical_under_tr_TR_and_invariant_culture()
    {
        // The strongest statement: the locale makes NO difference. If a culture-sensitive comparison ever crept
        // in, these two would diverge.
        var (_, tr) = RunUnderCulture("tr-TR", CaseInsensitiveSource);
        var (_, inv) = RunUnderCulture("", CaseInsensitiveSource); // invariant

        Assert.Equal(inv, tr);
    }
}
