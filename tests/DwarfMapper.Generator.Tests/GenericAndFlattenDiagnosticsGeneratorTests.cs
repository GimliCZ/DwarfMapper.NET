// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Silent-failure footguns made loud:
    ///     • DWARF053 — a generic mapping METHOD (arity &gt; 0) cannot be implemented; the generator would emit a
    ///     type-parameter-less body that fails to satisfy the declaration. Refuse loudly and skip the method.
    ///     • DWARF054 — [DwarfMapper] on a generic CLASS would generate a `partial class Foo` with no `&lt;T&gt;`,
    ///     which is not a partial of the user's type. Refuse loudly and skip generation.
    ///     • DWARF044 (reused) — a [Flatten] over a nullable-reference root emits unguarded `src.Root.Leaf` that
    ///     NREs at runtime; the dotted [MapProperty] path already warns DWARF044, so [Flatten] must too.
    /// </summary>
    public class GenericAndFlattenDiagnosticsGeneratorTests
    {
        private static Diagnostic? Find(IEnumerable<Diagnostic> diags, string id)
        {
            return diags.FirstOrDefault(d => d.Id == id);
        }

        // ── DWARF053 — generic mapping method ───────────────────────────────────────

        [Fact]
        public void Generic_mapping_method_reports_DWARF053_and_emits_no_body()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int Id { get; set; } }
                               public class D { public int Id { get; set; } }
                               [DwarfMapper] public partial class M
                               {
                                   public partial D Map<T>(S s);
                               }
                               """;
            var (diags, gen) = GeneratorTestHarness.Run(src);
            var d053 = Find(diags, "DWARF053");
            Assert.NotNull(d053);
            Assert.Equal(DiagnosticSeverity.Error, d053.Severity);
            // The generic method must be skipped — no implementation body is emitted for it.
            Assert.DoesNotContain("D Map<T>", gen, StringComparison.Ordinal);
        }

        // ── DWARF054 — generic mapper class ─────────────────────────────────────────

        [Fact]
        public void Generic_mapper_class_reports_DWARF054_and_emits_nothing()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int Id { get; set; } }
                               public class D { public int Id { get; set; } }
                               [DwarfMapper] public partial class M<T>
                               {
                                   public partial D Map(S s);
                               }
                               """;
            var (diags, gen) = GeneratorTestHarness.Run(src);
            var d054 = Find(diags, "DWARF054");
            Assert.NotNull(d054);
            Assert.Equal(DiagnosticSeverity.Error, d054.Severity);
            // Generation is skipped entirely for a generic class — no partial implementation emitted.
            Assert.Equal(string.Empty, gen);
        }

        // ── DWARF044 — [Flatten] over a nullable reference root ──────────────────────

        [Fact]
        public void Flatten_over_nullable_root_reports_DWARF044()
        {
            const string src = """
                               using DwarfMapper;
                               #nullable enable
                               namespace Demo;
                               public class Address { public string City { get; set; } = ""; }
                               public class Customer { public Address? Address { get; set; } }
                               public class CustomerDto { public string City { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [Flatten("Address")]
                                   public partial CustomerDto ToDto(Customer c);
                               }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            var d044 = Find(diags, "DWARF044");
            Assert.NotNull(d044);
            // Item 8: a nullable interior hop can NRE at runtime → Warning (still not build-breaking unless
            // -warnaserror), matching the dotted-path behavior.
            Assert.Equal(DiagnosticSeverity.Warning, d044.Severity);
        }

        [Fact]
        public void Flatten_over_non_nullable_root_does_not_report_DWARF044()
        {
            const string src = """
                               using DwarfMapper;
                               #nullable enable
                               namespace Demo;
                               public class Address { public string City { get; set; } = ""; }
                               public class Customer { public Address Address { get; set; } = new(); }
                               public class CustomerDto { public string City { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [Flatten("Address")]
                                   public partial CustomerDto ToDto(Customer c);
                               }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            Assert.Null(Find(diags, "DWARF044"));
        }

        /// <summary>
        ///     B26 (round 22 W2): the warning is about an unguarded <c>src.Root.Leaf</c>, so it may not fire when
        ///     no such access exists. Here the root is nullable and resolves, but not one of its leaves matches a
        ///     destination member — the flatten pulls up nothing, and warning that "a null value throws at runtime
        ///     when its flattened members are read" would be about members nobody reads. Before the fix the
        ///     warning fired the moment the root resolved, which is how <c>D10</c> read <c>Refused</c> at CreateMap
        ///     and UpdateInto for four rounds while the emitted output was byte-identical: a diagnostic on a
        ///     directive with no effect is a cell that looks measured and is not.
        /// </summary>
        [Fact]
        public void Flatten_over_nullable_root_whose_leaves_land_nowhere_does_not_report_DWARF044()
        {
            const string src = """
                               using DwarfMapper;
                               #nullable enable
                               namespace Demo;
                               public class Address { public string City { get; set; } = ""; }
                               public class Customer { public int Id { get; set; } public Address? Address { get; set; } }
                               public class CustomerDto { public int Id { get; set; } }
                               [DwarfMapper] public partial class M
                               {
                                   [Flatten("Address")]
                                   public partial CustomerDto ToDto(Customer c);
                               }
                               """;
            var (diags, generated) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            Assert.Null(Find(diags, "DWARF044"));
            // The evidence that nothing was pulled up: no `c.Address.` access exists to be unguarded.
            Assert.DoesNotContain("Address.", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The other direction of B26, and the reason the fix is a consumption test rather than a deletion:
        ///     when only ONE of two nullable roots actually lands a leaf, exactly that one warns. A blanket
        ///     "resolved, therefore warn" reports both; a blanket "never warn" reports neither, and the real
        ///     unguarded dereference goes quiet.
        /// </summary>
        [Fact]
        public void Only_the_nullable_flatten_root_that_really_lands_a_leaf_reports_DWARF044()
        {
            const string src = """
                               using DwarfMapper;
                               #nullable enable
                               namespace Demo;
                               public class Address { public string City { get; set; } = ""; }
                               public class Billing { public string Iban { get; set; } = ""; }
                               public class Customer
                               {
                                   public Address? Address { get; set; }
                                   public Billing? Billing { get; set; }
                               }
                               public class CustomerDto { public string City { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [Flatten("Address")]
                                   [Flatten("Billing")]
                                   public partial CustomerDto ToDto(Customer c);
                               }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            var reported = diags.Where(d => d.Id == "DWARF044")
                .Select(d => d.GetMessage(CultureInfo.InvariantCulture))
                .ToList();
            Assert.Single(reported);
            Assert.Contains("'Address'", reported[0], StringComparison.Ordinal);
            Assert.DoesNotContain("'Billing'", reported[0], StringComparison.Ordinal);
        }
    }
}
