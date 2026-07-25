// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     DWARF076 — a declared create-map whose source and target are the SAME type.
///     <para>
///         <c>[GenerateMap&lt;Dto, Dto&gt;]</c> (or <c>partial Dto Map(Dto s)</c>) compiles and quietly emits a
///         shallow copy. That is almost always a copy-paste slip — the author meant <c>Entity → Dto</c> — and the
///         completeness gate cannot catch it, because a type trivially satisfies itself: every destination member
///         has a same-named source member, so the map is "complete" and silent. A shallow clone is a legitimate
///         thing to want, so this is a Warning (a warnings-as-errors build still fails; a deliberate clone
///         suppresses the id) rather than a hard error.
///     </para>
///     <para>
///         Scope matters as much as the rule: <c>Update(T src, T dest)</c> — copying values onto an EXISTING
///         instance of the same type — is a genuinely common pattern (refreshing a tracked entity from a detached
///         one), so it is deliberately exempt. Auto-synthesized nested pairs are exempt too: a same-type nested
///         member is the graph's shape, not the author's typo, and there would be nothing to fix.
///     </para>
/// </summary>
public class SelfMapDiagnosticTests
{
    private const string Id = "DWARF076";

    [Fact]
    public void GenerateMap_with_identical_source_and_target_reports_DWARF076()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public sealed class Dto { public int Id { get; set; } public string Text { get; set; } = ""; }

            [DwarfMapper]
            [GenerateMap<Dto, Dto>]
            public partial class M { }
            """;

        var reported = GeneratorAssert.Reports(src, Id);

        Assert.Contains(reported, d => d.Severity == DiagnosticSeverity.Warning);
        Assert.Contains(reported, d =>
            d.GetMessage(CultureInfo.InvariantCulture).Contains("Demo.Dto", StringComparison.Ordinal));
    }

    [Fact]
    public void Partial_create_method_with_identical_source_and_target_reports_DWARF076()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public sealed class Dto { public int Id { get; set; } }

            [DwarfMapper]
            public partial class M { public partial Dto Map(Dto s); }
            """;

        Assert.NotEmpty(GeneratorAssert.Reports(src, Id));
    }

    [Fact]
    public void Distinct_source_and_target_does_not_report_DWARF076()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public sealed class Src { public int Id { get; set; } }
            public sealed class Dst { public int Id { get; set; } }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            public partial class M { }
            """;

        GeneratorAssert.DoesNotReport(src, Id);
    }

    [Fact]
    public void Update_into_existing_of_the_same_type_is_exempt()
    {
        // Copying values onto an existing instance of the same type is a real pattern (refresh a tracked
        // entity from a detached one), so it must stay silent.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public sealed class Order { public int Id { get; set; } public string Note { get; set; } = ""; }

            [DwarfMapper]
            public partial class M
            {
                public partial void Update(Order src, Order dest);
            }
            """;

        GeneratorAssert.DoesNotReport(src, Id);
    }

    [Fact]
    public void Auto_synthesized_same_type_nested_pair_is_exempt()
    {
        // Src -> Dst is a genuine map, but the Child member is Leaf on BOTH sides, so the generator
        // synthesizes a Leaf -> Leaf nested pair. The author wrote no such map and could not "fix" it —
        // warning here would be unactionable noise.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public sealed class Leaf { public int V { get; set; } }
            public sealed class Src { public int Id { get; set; } public Leaf Child { get; set; } = new(); }
            public sealed class Dst { public int Id { get; set; } public Leaf Child { get; set; } = new(); }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            public partial class M { }
            """;

        GeneratorAssert.DoesNotReport(src, Id);
    }

    [Fact]
    public void DWARF076_is_suppressible_for_a_deliberate_clone()
    {
        // The escape hatch has to actually work: a deliberate shallow-clone map silences the id via
        // .editorconfig severity=none, and the mapper still generates and compiles.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public sealed class Dto { public int Id { get; set; } }

            [DwarfMapper]
            [GenerateMap<Dto, Dto>]
            public partial class M { }
            """;

        var generated = GeneratorAssert.EmitsCompilableCode(src);

        Assert.Contains("Dto", generated, StringComparison.Ordinal);
    }
}
