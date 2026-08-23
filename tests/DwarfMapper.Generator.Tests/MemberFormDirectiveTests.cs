// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>[MapProperty]</c> and <c>[MapIgnore]</c> each carry TWO placements behind one name, and each
    ///     placement has its own constructor. The member form is the <c>[MapTo]</c> registry / co-located host
    ///     form — <c>[MapProperty("Destination")]</c> names what the ANNOTATED MEMBER supplies, and a bare
    ///     <c>[MapIgnore]</c> says "never read the ANNOTATED MEMBER". Written on a mapper class or a mapping
    ///     method, where there is no annotated member, neither one means anything.
    ///     <para>
    ///         Both used to be discarded in silence: <c>ReadExplicitMaps</c> accepts only the two-argument
    ///         application and <c>ReadIgnores</c> only the one-argument application, and each simply skipped
    ///         anything else. One arity check for both (<c>DWARF088</c>) is what these pin — they are one defect
    ///         wearing two attribute names, which is why they share a file and an id.
    ///     </para>
    /// </summary>
    public sealed class MemberFormDirectiveTests
    {
        /// <summary>
        ///     The one-argument <c>[MapProperty]</c> on a mapping method. Its constructor sets
        ///     <c>Source = Target = the one name</c>, so honouring it would emit the identity binding
        ///     auto-matching already produces — byte-identical output by construction. That is exactly why a
        ///     refusal is the only way a caller can find out: whichever way the generator reads this, the caller
        ///     used the wrong overload and the result cannot be what they meant.
        /// </summary>
        [Fact]
        public void Member_form_MapProperty_on_a_mapping_method_reports_DWARF088()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public string Name { get; set; } = ""; }
                               public class Target { public string Name { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty("Name")]
                                   public partial Target Map(Source s);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF088" &&
                     d.GetMessage(CultureInfo.InvariantCulture)
                         .Contains("[MapProperty(\"Name\", ", StringComparison.Ordinal));
        }

        /// <summary>
        ///     The named-argument payload rides on the SAME one-argument constructor —
        ///     <c>[MapProperty("Name", Use = "F")]</c> is <c>ctor(1)</c> plus a property initializer, not a second
        ///     overload. So the whole directive was dropped and the converter, predicate, null substitute and
        ///     format string went with it: the caller named a conversion method and got auto-matching, silently.
        /// </summary>
        [Fact]
        public void Member_form_MapProperty_carrying_a_converter_is_refused_rather_than_dropped()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public string Name { get; set; } = ""; }
                               public class Target { public string Name { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty("Name", Use = nameof(Shout))]
                                   public partial Target Map(Source s);

                                   private static string Shout(string s) => s.ToUpperInvariant();
                               }
                               """;
            var (diagnostics, generated) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics, d => d.Id == "DWARF088");

            // The reading that makes the refusal necessary rather than merely tidy: the converter was never
            // called. Asserted so a future change that starts HONOURING this form has to come here and say so.
            Assert.DoesNotContain("Shout(", generated, StringComparison.Ordinal);
        }

        /// <summary>The two-argument method form is the supported one and must stay silent.</summary>
        [Fact]
        public void Method_form_MapProperty_is_not_refused()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public string FullName { get; set; } = ""; }
                               public class Target { public string Name { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty("FullName", "Name")]
                                   public partial Target Map(Source s);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF088");
        }

        /// <summary>
        ///     The no-argument <c>[MapIgnore]</c> on a mapping method. It names nothing, so it excludes nothing —
        ///     and the completeness gate goes on demanding the member the caller believes they excluded, which is
        ///     the one symptom that could have led them to the truth. It does not: they get <c>DWARF001</c> for a
        ///     member they thought they had already answered for.
        /// </summary>
        [Fact]
        public void Member_form_MapIgnore_on_a_mapping_method_reports_DWARF088()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public string Name { get; set; } = ""; }
                               public class Target { public string Name { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapIgnore]
                                   public partial Target Map(Source s);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF088" &&
                     d.GetMessage(CultureInfo.InvariantCulture)
                         .Contains("[MapIgnore(", StringComparison.Ordinal));
        }

        /// <summary>
        ///     The same directive at the CLASS site, which is a separate reader (<c>classIgnores</c>) and would
        ///     have needed a separate check had the two not been folded into one.
        /// </summary>
        [Fact]
        public void Member_form_MapIgnore_on_a_mapper_class_reports_DWARF088()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public string Name { get; set; } = ""; }
                               public class Target { public string Name { get; set; } = ""; }
                               [DwarfMapper]
                               [MapIgnore]
                               public partial class M
                               {
                                   public partial Target Map(Source s);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics, d => d.Id == "DWARF088");
        }

        /// <summary>The one-argument class/method form is the supported one and must stay silent.</summary>
        [Fact]
        public void Method_form_MapIgnore_is_not_refused()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public string Name { get; set; } = ""; }
                               public class Target { public string Name { get; set; } = ""; public int Extra { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapIgnore("Extra")]
                                   public partial Target Map(Source s);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF088");
        }

        /// <summary>
        ///     An ERROR, and the consequence is pinned with it: the class stops emitting, so the identity binding
        ///     the caller would silently have received is not there.
        ///     <para>
        ///         This id shipped as a Warning for one round on a reason that was never a product reason — a
        ///         blocking DwarfMapper error suppresses the whole class's emission, so the refusal arrives
        ///         alongside <c>CS8795</c> from the unimplemented partial method, and while the R4 ordering defect
        ///         stood the surface matrix read that cascade as "the compiler rejected the placement" rather than
        ///         as a refusal. R4 is fixed. The cascade is paid by every blocking id on this class,
        ///         <c>DWARF011</c> and <c>DWARF087</c> included, and both of those are Errors; a cost every id
        ///         pays cannot decide the severity of one of them.
        ///     </para>
        ///     <para>
        ///         Both halves are asserted because the escalation is exactly the difference between them: a test
        ///         that only read the severity would pass on a generator that reported an Error and emitted the
        ///         silent binding anyway.
        ///     </para>
        /// </summary>
        [Fact]
        public void The_refusal_is_an_error_and_the_class_stops_emitting()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public string Name { get; set; } = ""; }
                               public class Target { public string Name { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty("Name")]
                                   public partial Target Map(Source s);
                               }
                               """;
            var (diagnostics, generated) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF088" && d.Severity == DiagnosticSeverity.Error);
            Assert.DoesNotContain("Name = s.Name", generated, StringComparison.Ordinal);
        }
    }
}
