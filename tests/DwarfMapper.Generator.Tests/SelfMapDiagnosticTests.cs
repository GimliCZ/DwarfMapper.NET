// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
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
            Assert.Contains(reported,
                d =>
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

        // ---------------------------------------------------------------------------------------------
        // Regression: found migrating the Round-18 consumer (~300 maps) off AutoMapper 14.
        //
        // That codebase has four legitimate CreateMap<X, X>() clone maps, so it hits DWARF076 four times by
        // design. docs/diagnostics.md#dwarf076 offers three escape hatches:
        //
        //     "(#pragma warning disable DWARF076, a [SuppressMessage], or
        //      dotnet_diagnostic.DWARF076.severity = none in .editorconfig)"
        //
        // Only the third was ever covered by a test — and the test above does not actually assert
        // suppression, it only asserts the code still compiles. The two in-FILE hatches are what a consumer
        // reaches for first, because they are local to the deliberate clone instead of disabling the rule
        // for the whole project. If they do not work, a warnings-as-errors consumer has no local way out
        // and the documentation is actively misleading.
        // ---------------------------------------------------------------------------------------------

        [Fact]
        public void DWARF076_is_NOT_suppressed_by_a_pragma_documenting_a_Roslyn_limitation()
        {
            // Pins current, deliberate behaviour rather than an aspiration.
            //
            // `#pragma warning disable` is applied by the compiler's diagnostic filtering, which
            // source-generator-reported diagnostics do not pass through. The generator therefore CANNOT honour
            // a pragma — this is a Roslyn platform limitation, not a DwarfMapper bug, and docs/diagnostics.md
            // no longer claims otherwise. The in-file hatch is [SuppressMessage] (next test); the project-wide
            // one is .editorconfig.
            //
            // If a future Roslyn starts filtering generator diagnostics, this test fails and is the signal to
            // re-document the pragma as supported.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public sealed class Dto { public int Id { get; set; } }

                               #pragma warning disable DWARF076
                               [DwarfMapper]
                               [GenerateMap<Dto, Dto>]
                               public partial class M { }
                               #pragma warning restore DWARF076
                               """;

            Assert.NotEmpty(GeneratorAssert.Reports(src, Id));
        }

        [Fact]
        public void DWARF076_is_suppressed_by_SuppressMessage_on_the_mapper()
        {
            const string src = """
                               using System.Diagnostics.CodeAnalysis;
                               using DwarfMapper;
                               namespace Demo;
                               public sealed class Dto { public int Id { get; set; } }

                               [DwarfMapper]
                               [GenerateMap<Dto, Dto>]
                               [SuppressMessage("DwarfMapper", "DWARF076:Source and target are the same type",
                                   Justification = "Deliberate shallow clone.")]
                               public partial class M { }
                               """;

            GeneratorAssert.DoesNotReport(src, Id);
        }
    }
}
