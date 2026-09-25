// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Deep source paths in [MapProperty] (Phase 3): a dotted source like "Customer.Name" reads through the
    ///     object graph (member names never contain dots, so it is unambiguous). The leaf type drives the
    ///     conversion; the path is emitted verbatim (s.Customer.Name). An unknown segment is DWARF043; a nullable
    ///     interior hop (can NRE at runtime) is the DWARF044 suggestion.
    /// </summary>
    public class DeepSourcePathGeneratorTests
    {
        private static Diagnostic? Find(IEnumerable<Diagnostic> diags, string id)
        {
            return diags.FirstOrDefault(d => d.Id == id);
        }

        [Fact]
        public void Two_hop_path_reads_through_and_compiles()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Customer { public string Name { get; set; } = ""; }
                               public class S { public Customer Customer { get; set; } = new(); }
                               public class D { public string CustomerName { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Customer.Name", nameof(D.CustomerName))]
                                   public partial D Map(S s);
                               }
                               """;
            var gen = GeneratorAssert.CompilesClean(src);
            Assert.Contains("s.Customer.Name", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void Three_hop_path_compiles()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class City { public string Name { get; set; } = ""; }
                               public class Addr { public City City { get; set; } = new(); }
                               public class S { public Addr Address { get; set; } = new(); }
                               public class D { public string Town { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Address.City.Name", nameof(D.Town))]
                                   public partial D Map(S s);
                               }
                               """;
            var (_, gen) = GeneratorTestHarness.Run(src);
            Assert.Contains("s.Address.City.Name", gen, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void Path_with_leaf_conversion_uses_converter()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Customer { public int Age { get; set; } }
                               public class S { public Customer Customer { get; set; } = new(); }
                               public class D { public string CustomerAge { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Customer.Age", nameof(D.CustomerAge))]
                                   public partial D Map(S s);
                               }
                               """;
            var (_, gen) = GeneratorTestHarness.Run(src);
            Assert.Contains("s.Customer.Age", gen, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void Path_with_explicit_Use_converter_compiles()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Customer { public string Name { get; set; } = ""; }
                               public class S { public Customer Customer { get; set; } = new(); }
                               public class D { public int NameLen { get; set; } }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Customer.Name", nameof(D.NameLen), Use = nameof(Len))]
                                   public partial D Map(S s);
                                   private static int Len(string v) => v.Length;
                               }
                               """;
            var (_, gen) = GeneratorTestHarness.Run(src);
            Assert.Contains("Len(s.Customer.Name)", gen, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void Unknown_segment_reports_DWARF043()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Customer { public string Name { get; set; } = ""; }
                               public class S { public Customer Customer { get; set; } = new(); }
                               public class D { public string CustomerCity { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Customer.City", nameof(D.CustomerCity))]
                                   public partial D Map(S s);
                               }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            var d = Find(diags, "DWARF043");
            Assert.NotNull(d);
            // Names BOTH the path the caller wrote and the segment that broke it — the second is the part a
            // five-hop path needs, and the descriptor's format is a bare "{0}", so only the text carries it.
            Assert.Contains("source path 'Customer.City' has no member 'City'",
                d.GetMessage(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
            // The walk failed, so the explicit map `continue`s before the flat-name lookup. Without that, the
            // null leaf type would fall into the "unknown source" arm and the one bad segment would be
            // reported twice, once as a path and once as a member that does not exist.
            Assert.Null(Find(diags, "DWARF009"));
        }

        [Fact]
        public void Unknown_root_segment_reports_DWARF043()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int Id { get; set; } }
                               public class D { public string X { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Nope.Name", nameof(D.X))]
                                   public partial D Map(S s);
                               }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            var d = Find(diags, "DWARF043");
            Assert.NotNull(d);
            Assert.Contains("source path 'Nope.Name' has no member 'Nope'",
                d.GetMessage(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        [Fact]
        public void Nullable_interior_hop_reports_DWARF044_warning()
        {
            const string src = """
                               using DwarfMapper;
                               #nullable enable
                               namespace Demo;
                               public class Customer { public string Name { get; set; } = ""; }
                               public class S { public Customer? Customer { get; set; } }
                               public class D { public string CustomerName { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Customer.Name", nameof(D.CustomerName))]
                                   public partial D Map(S s);
                               }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            var d = Find(diags, "DWARF044");
            Assert.NotNull(d);
            // Item 8: a nullable interior hop can NRE at runtime → Warning, not a mere suggestion.
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            // The message is the whole diagnostic (a bare "{0}" descriptor): it must name the path and say
            // what the hazard is, or the warning is a code with nothing to act on.
            Assert.Contains("source path 'Customer.Name' traverses a nullable member; a null interior value throws at runtime",
                d.GetMessage(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        [Fact]
        public void Non_nullable_path_does_not_report_DWARF044()
        {
            const string src = """
                               using DwarfMapper;
                               #nullable enable
                               namespace Demo;
                               public class Customer { public string Name { get; set; } = ""; }
                               public class S { public Customer Customer { get; set; } = new(); }
                               public class D { public string CustomerName { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Customer.Name", nameof(D.CustomerName))]
                                   public partial D Map(S s);
                               }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            Assert.Null(Find(diags, "DWARF044"));
        }

        // ── A dotted path into a CONSTRUCTOR PARAMETER (R18-31) ──────────────────────────────────────────────
        // Every test above binds a path to a settable property. Binding one to a constructor parameter went down
        // a different branch that did a flat name lookup, so the path never matched and DWARF009 claimed the
        // member "does not exist or is not readable" — about a member that existed, was readable, and mapped
        // correctly into a property one line up. Found by the DDD corpus in tests/DwarfMapper.ConsumerTests,
        // where a positional record view is fed from a value object, which is the ordinary DDD shape.

        [Fact]
        public void Dotted_path_into_a_constructor_parameter_reads_through_and_compiles()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public sealed record Window(int Start, int End);
                               public class S
                               {
                                   public string Room { get; set; } = "";
                                   public Window Window { get; set; } = new(0, 0);
                               }
                               public sealed record D(string Room, int Start, int End);
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Window.Start", "Start")]
                                   [MapProperty("Window.End", "End")]
                                   public partial D Map(S s);
                               }
                               """;
            var gen = GeneratorAssert.CompilesClean(src);
            Assert.Contains("Start: s.Window.Start", gen, StringComparison.Ordinal);
            Assert.Contains("End: s.Window.End", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void Dotted_path_into_a_constructor_parameter_honours_an_explicit_Use_converter()
        {
            // The converter takes the LEAF type, exactly as it does for a member target — and it is reserved to
            // the parameter that names it, which is where the converter-scoping regression lived.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public sealed record Window(int Start, int End);
                               public class S { public Window Window { get; set; } = new(0, 0); }
                               public sealed record D(string Start);
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Window.Start", "Start", Use = nameof(Fmt))]
                                   public partial D Map(S s);
                                   private static string Fmt(int v) => "#" + v;
                               }
                               """;
            var gen = GeneratorAssert.CompilesClean(src);
            Assert.Contains("Fmt(s.Window.Start)", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void Unknown_segment_into_a_constructor_parameter_reports_DWARF043_and_not_DWARF009()
        {
            // The message is the point. DWARF043 names the segment that is actually missing; DWARF009 would say
            // the source member does not exist, sending the reader to hunt for a typo in a name that is correct.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public sealed record Window(int Start, int End);
                               public class S { public Window Window { get; set; } = new(0, 0); }
                               public sealed record D(int Start);
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Window.Middle", "Start")]
                                   public partial D Map(S s);
                               }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            var d = Find(diags, "DWARF043");
            Assert.NotNull(d);
            Assert.Contains("Middle", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.Null(Find(diags, "DWARF009"));
        }

        [Fact]
        public void Nullable_interior_hop_into_a_constructor_parameter_reports_DWARF044()
        {
            const string src = """
                               using DwarfMapper;
                               #nullable enable
                               namespace Demo;
                               public sealed record Window(int Start, int End);
                               public class S { public Window? Window { get; set; } }
                               public sealed record D(int Start);
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Window.Start", "Start")]
                                   public partial D Map(S s);
                               }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            var d = Find(diags, "DWARF044");
            Assert.NotNull(d);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        }

        // Fuzz: paths of increasing depth all resolve and compile.
        // CA1305: this method only assembles C# source text (ASCII identifiers / fixed literals) — there is
        // no locale-sensitive formatting, so the invariant-culture overloads add only noise here.
#pragma warning disable CA1305
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void Paths_of_varying_depth_compile(int depth)
        {
            // Build a chain N0 -> N1 -> ... where each Ni has a `Next` of type N(i+1), leaf has `Value`.
            var sb = new StringBuilder();
            sb.AppendLine("using DwarfMapper;");
            sb.AppendLine("namespace Demo;");
            for (var i = 0; i < depth; i++)
            {
                var next = i < depth - 1
                    ? $"public N{i + 1} Next {{ get; set; }} = new();"
                    : "public string Value { get; set; } = \"\";";
                sb.AppendLine(CultureInfo.InvariantCulture, $"public class N{i} {{ {next} }}");
            }

            sb.AppendLine("public class S { public N0 Root { get; set; } = new(); }");
            sb.AppendLine("public class D { public string Leaf { get; set; } = \"\"; }");
            var path = "Root." + string.Join(".", Enumerable.Repeat("Next", depth - 1)) + (depth > 1 ? ".Value" : "");
            // depth>=2 guarantees at least Root.Value or Root.Next...Value
            if (depth == 1)
            {
                path = "Root.Value";
            }

            sb.AppendLine(
                $"[DwarfMapper] public partial class M {{ [MapProperty(\"{path}\", nameof(D.Leaf))] public partial D Map(S s); }}");
            var src = sb.ToString();

            GeneratorAssert.CompilesClean(src);
        }
#pragma warning restore CA1305
        [Fact]
        public void A_source_name_that_only_STARTS_with_a_dot_is_not_a_path_and_names_itself()
        {
            // A dot INSIDE a name makes it a path; a dot at position 0 does not, because there is no first
            // segment for it to separate. Reading ".Name" as a path walks to an empty segment and reports
            // DWARF043 "source path '.Name' has no member ''" - which blames a member the consumer never wrote
            // and hides the real mistake, the stray dot. The name is reported as what it literally is instead.
            //
            // The sibling test above pins the opposite for a REAL path ("Window.Middle" is DWARF043, not
            // DWARF009); together they say where the boundary is.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public string Name { get; set; } = ""; }
                               public class D { public string Full { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty(".Name", "Full")]
                                   public partial D Map(S s);
                               }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src);

            var d = Find(diags, "DWARF009");
            Assert.NotNull(d);
            Assert.Contains("'.Name'", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.Null(Find(diags, "DWARF043"));
        }

        [Fact]
        public void A_target_name_that_only_STARTS_with_a_dot_is_not_an_unflatten_and_names_itself()
        {
            // The destination half of the same rule. Read as an unflatten, ".Full" reports DWARF045
            // "unflatten intermediate '' is not a writable destination member" - and consumes 'Full' on the way,
            // so the reader is told about an empty intermediate they never wrote. Reported as a plain unknown
            // destination member, the message names '.Full' and the stray dot is visible.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public string Name { get; set; } = ""; }
                               public class D { public string Full { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Name", ".Full")]
                                   public partial D Map(S s);
                               }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src);

            var d = Find(diags, "DWARF008");
            Assert.NotNull(d);
            Assert.Contains("'.Full'", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.Null(Find(diags, "DWARF045"));
        }

    }
}
