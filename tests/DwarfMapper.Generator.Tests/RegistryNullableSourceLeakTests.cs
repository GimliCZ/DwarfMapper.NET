// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     The <c>[MapTo]</c> registry wrote a nullable reference straight into a non-nullable one —
    ///     <c>Name = source.Name,</c> for <c>string? Name</c> into <c>string Name</c> — so the consumer's build got
    ///     <c>CS8601</c> (and <c>CS8604</c> for a collection element) from inside <c>__DwarfRegistry_*.g.cs</c>, a file
    ///     they cannot edit, and no DwarfMapper diagnostic said anything. The only way out was a project-wide
    ///     <c>&lt;NoWarn&gt;CS8601&lt;/NoWarn&gt;</c>, which silences every real CS8601 in the project too.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Found 2026-10-11 while deciding whether <c>[MapProperty(AllowNull = true)]</c> has anything to accept
    ///         at the registry. It hid behind a corpus hole: <c>GeneratedCodeIsWarningFreeTests</c> runs the registry
    ///         only where a test opts in (<c>includeRegistry</c>), and the one registry case there maps
    ///         <c>Child?</c> into <c>ChildDto?</c> — nullable into nullable, the one combination that cannot leak.
    ///     </para>
    ///     <para>
    ///         The fix is the class model's rule for a raw assignment (<c>MemberMap.NullRefIntoNonNullable</c>):
    ///         null-forgive the access so the generated file compiles clean, and say so in DwarfMapper's own voice -
    ///         <c>DWARFR15</c>, the registry twin of <c>DWARF070</c>, with remedies that exist at this front door.
    ///         A nested object goes through a synthesized helper that is null-TOLERANT, which is the class model's
    ///         recorded exception too, so it is pinned here as unchanged rather than reported.
    ///     </para>
    /// </remarks>
    public class RegistryNullableSourceLeakTests
    {
        private const string Member = """
                                      using DwarfMapper;
                                      namespace T;
                                      [MapTo(typeof(Dst))] public class Src { public string? Name { get; set; } public int Id { get; set; } }
                                      public class Dst { public string Name { get; set; } = ""; public int Id { get; set; } }
                                      """;

        private const string ListElement = """
                                           using System.Collections.Generic;
                                           using DwarfMapper;
                                           namespace T;
                                           [MapTo(typeof(Dst))] public class Src { public List<string?> Tags { get; set; } = new(); }
                                           public class Dst { public List<string> Tags { get; set; } = new(); }
                                           """;

        private const string ArrayElement = """
                                            using DwarfMapper;
                                            namespace T;
                                            [MapTo(typeof(Dst))] public class Src { public string?[] Tags { get; set; } = new string?[2]; }
                                            public class Dst { public string[] Tags { get; set; } = System.Array.Empty<string>(); }
                                            """;

        private const string NestedMember = """
                                            using DwarfMapper;
                                            namespace T;
                                            [MapTo(typeof(Dst))] public class Src { public Child Child { get; set; } = new(); }
                                            public class Child { public string? Name { get; set; } }
                                            public class Dst { public ChildDto Child { get; set; } = new(); }
                                            public class ChildDto { public string Name { get; set; } = ""; }
                                            """;

        private const string ImplicitToObject = """
                                                using DwarfMapper;
                                                namespace T;
                                                [MapTo(typeof(Dst))] public class Src { public string? Name { get; set; } }
                                                public class Dst { public object Name { get; set; } = ""; }
                                                """;

        private const string DictionaryValue = """
                                               using System.Collections.Generic;
                                               using DwarfMapper;
                                               namespace T;
                                               [MapTo(typeof(Dst))] public class Src { public Dictionary<string, string?> Lookup { get; set; } = new(); }
                                               public class Dst { public Dictionary<string, string> Lookup { get; set; } = new(); }
                                               """;

        private const string InterfaceView = """
                                             using System.Collections.Generic;
                                             using DwarfMapper;
                                             namespace T;
                                             [MapTo(typeof(Dst))] public class Src { public List<string?> Tags { get; set; } = new(); }
                                             public class Dst { public IReadOnlyList<string> Tags { get; set; } = new List<string>(); }
                                             """;

        // No null can land - every source element is non-null - but List<T> is invariant, so C# still warns
        // (CS8619) about the annotation mismatch. Forgiven, and NOT reported: there is no risk to report.
        private const string InvariantWidening = """
                                                 using System.Collections.Generic;
                                                 using DwarfMapper;
                                                 namespace T;
                                                 [MapTo(typeof(Dst))] public class Src { public List<string> Tags { get; set; } = new(); }
                                                 public class Dst { public List<string?> Tags { get; set; } = new(); }
                                                 """;

        // A null two type arguments deep lands exactly as one does: reported as the member's element, the position the
        // consumer edits.
        private const string NestedElement = """
                                             using System.Collections.Generic;
                                             using DwarfMapper;
                                             namespace T;
                                             [MapTo(typeof(Dst))] public class Src { public List<List<string?>> Tags { get; set; } = new(); }
                                             public class Dst { public List<List<string>> Tags { get; set; } = new(); }
                                             """;

        // The destination IS the dictionary interface, not a type implementing it.
        private const string DictionaryInterfaceValue = """
                                                        using System.Collections.Generic;
                                                        using DwarfMapper;
                                                        namespace T;
                                                        [MapTo(typeof(Dst))] public class Src { public Dictionary<string, string?> Lookup { get; set; } = new(); }
                                                        public class Dst { public IReadOnlyDictionary<string, string> Lookup { get; set; } = new Dictionary<string, string>(); }
                                                        """;

        // A contravariant (in) argument consumes values instead of holding them: a comparer that accepts null is a
        // comparer of non-null strings, so nothing lands and C# has nothing to say.
        private const string Contravariant = """
                                             using System.Collections.Generic;
                                             using DwarfMapper;
                                             namespace T;
                                             [MapTo(typeof(Dst))] public class Src { public IComparer<string?> Order { get; set; } = Comparer<string?>.Default; }
                                             public class Dst { public IComparer<string> Order { get; set; } = Comparer<string>.Default; }
                                             """;

        // Value-type arguments carry no reference annotation at all.
        private const string ValueTypeArguments = """
                                                  using System.Collections.Generic;
                                                  using DwarfMapper;
                                                  namespace T;
                                                  [MapTo(typeof(Dst))] public class Src { public Dictionary<int, int?> Counts { get; set; } = new(); }
                                                  public class Dst { public Dictionary<int, int?> Counts { get; set; } = new(); }
                                                  """;

        public static TheoryData<string, string> LeakingShapes() => new()
        {
            { "member", Member },
            { "List element", ListElement },
            { "array element", ArrayElement },
            { "nested member", NestedMember },
            { "implicit reference conversion", ImplicitToObject },
            { "dictionary value", DictionaryValue },
            { "interface view of a List", InterfaceView },
            { "invariant widening", InvariantWidening },
            { "element of an element", NestedElement },
            { "dictionary interface value", DictionaryInterfaceValue },
            { "contravariant argument", Contravariant },
            { "value-type arguments", ValueTypeArguments }
        };

        [Theory]
        [MemberData(nameof(LeakingShapes))]
        public void Registry_generated_code_leaks_no_nullable_warning(string shape, string source)
        {
            var warnings = GeneratorTestHarness.GeneratedCodeWarnings(source, includeRegistry: true);

            Assert.True(warnings.Length == 0,
                $"[MapTo] {shape}: the registry's generated file warns, and the consumer cannot edit it:\n  " +
                string.Join("\n  ", warnings.Select(w => w.Id + " " + w.GetMessage(CultureInfo.InvariantCulture))));
        }

        [Fact]
        public void A_nullable_member_is_null_forgiven_and_reported_as_DWARFR15()
        {
            var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(Member, NullableContextOptions.Enable);

            var d = Assert.Single(diagnostics, x => x.Id == "DWARFR15");
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.StartsWith("Source member 'Name' is a nullable reference", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.Contains("Name = source.Name!,", generated, StringComparison.Ordinal);
            // Only the edge that can carry a null: the int beside it is written exactly as before.
            Assert.Contains("Id = source.Id,", generated, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("List", ListElement)]
        [InlineData("array", ArrayElement)]
        [InlineData("interface view", InterfaceView)]
        [InlineData("element of an element", NestedElement)]
        public void A_nullable_element_is_reported_against_the_element_not_the_member(string shape, string source)
        {
            var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(source, NullableContextOptions.Enable);

            var d = Assert.Single(diagnostics, x => x.Id == "DWARFR15");
            Assert.True(d.GetMessage(CultureInfo.InvariantCulture)
                    .StartsWith("The source element mapped into 'Tags' is a nullable reference", StringComparison.Ordinal),
                shape + ": " + d.GetMessage(CultureInfo.InvariantCulture));
            Assert.Contains("Tags = source.Tags!,", generated, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(DictionaryValue)]
        [InlineData(DictionaryInterfaceValue)]
        public void A_nullable_dictionary_value_is_reported_against_the_value(string source)
        {
            var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(source, NullableContextOptions.Enable);

            var d = Assert.Single(diagnostics, x => x.Id == "DWARFR15");
            Assert.StartsWith("The source value mapped into 'Lookup' is a nullable reference",
                d.GetMessage(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
            Assert.Contains("Lookup = source.Lookup!,", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void An_invariant_widening_is_forgiven_but_not_reported_because_no_null_can_land()
        {
            var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(InvariantWidening, NullableContextOptions.Enable);

            Assert.DoesNotContain(diagnostics, x => x.Id == "DWARFR15");
            Assert.Contains("Tags = source.Tags!,", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_covariant_widening_needs_neither_the_forgiveness_nor_the_report()
        {
            // string[] into string?[]: arrays are covariant in nullability, so C# has nothing to say and neither do we.
            const string source = """
                                  using DwarfMapper;
                                  namespace T;
                                  [MapTo(typeof(Dst))] public class Src { public string[] Tags { get; set; } = System.Array.Empty<string>(); }
                                  public class Dst { public string?[] Tags { get; set; } = System.Array.Empty<string?>(); }
                                  """;

            var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(source, NullableContextOptions.Enable);

            Assert.DoesNotContain(diagnostics, x => x.Id == "DWARFR15");
            Assert.Contains("Tags = source.Tags,", generated, StringComparison.Ordinal);
            Assert.Empty(GeneratorTestHarness.GeneratedCodeWarnings(source, includeRegistry: true));
        }

        [Theory]
        [InlineData(Contravariant, "Order = source.Order,")]
        [InlineData(ValueTypeArguments, "Counts = source.Counts,")]
        public void Where_no_null_can_land_the_assignment_is_neither_forgiven_nor_reported(string source, string assignment)
        {
            var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(source, NullableContextOptions.Enable);

            Assert.DoesNotContain(diagnostics, x => x.Id == "DWARFR15");
            Assert.Contains(assignment, generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_nullable_member_of_a_nested_object_names_the_nested_type()
        {
            var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(NestedMember, NullableContextOptions.Enable);

            var d = Assert.Single(diagnostics, x => x.Id == "DWARFR15");
            Assert.StartsWith("Member 'Name' of nested type 'Child' is a nullable reference",
                d.GetMessage(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
            Assert.Contains("Name = s.Name!", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_non_nullable_source_is_untouched()
        {
            var source = Member.Replace("string? Name", "string Name { get; set; } = \"\"; public string Unused", StringComparison.Ordinal);

            var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(source, NullableContextOptions.Enable);

            Assert.DoesNotContain(diagnostics, x => x.Id == "DWARFR15");
            Assert.Contains("Name = source.Name,", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_nullable_destination_is_untouched()
        {
            var source = Member.Replace("public string Name { get; set; } = \"\";", "public string? Name { get; set; }", StringComparison.Ordinal);

            var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(source, NullableContextOptions.Enable);

            Assert.DoesNotContain(diagnostics, x => x.Id == "DWARFR15");
            Assert.Contains("Name = source.Name,", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_nullable_nested_object_keeps_the_null_tolerant_helper_unreported()
        {
            // The helper returns null for a null source, exactly as the class model's synthesized helpers do - the
            // recorded class-wide exception (round 29 T2.6-T2.9). Pinned so this fix does not quietly widen into it.
            const string source = """
                                  using DwarfMapper;
                                  namespace T;
                                  [MapTo(typeof(Dst))] public class Src { public Child? Child { get; set; } }
                                  public class Child { public int V { get; set; } }
                                  public class Dst { public ChildDto Child { get; set; } = new(); }
                                  public class ChildDto { public int V { get; set; } }
                                  """;

            var (diagnostics, _) = GeneratorTestHarness.RunMapToWithSource(source, NullableContextOptions.Enable);

            Assert.DoesNotContain(diagnostics, x => x.Id == "DWARFR15");
        }
    }
}
