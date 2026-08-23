// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     The generator's contract is over CONSUMER code, so every new C# language version is a new set of input
    ///     shapes it has never been asked about. C# 14 shipped `field`-backed properties, partial constructors,
    ///     extension members and user-defined compound assignment; before this file the corpus contained zero
    ///     instances of any of them, so "we still work on .NET 10" was an assumption, not a measurement.
    ///     <para>
    ///         That gap is the one this repository keeps finding: the bug hides in the corpus schema, not in the
    ///         generator. Raising the Roslyn floor to 5.0 is exactly the moment to close it, because 5.0 is the
    ///         first version whose symbol model even describes extension members
    ///         (<c>INamedTypeSymbol.IsExtension</c> and friends are new in 5.0 — measured against the 4.14 API
    ///         surface, along with <c>IMethodSymbol.IsIterator</c> and the 18 compound-assignment
    ///         <c>WellKnownMemberNames</c>).
    ///     </para>
    ///     <para>
    ///         Five of the six shapes below already behaved correctly when first probed. These tests are therefore
    ///         REGRESSION guards, not bug reports — they pin behaviour that works today so a future change to
    ///         member enumeration or constructor selection cannot quietly stop handling C# 14 input. The sixth is
    ///         pinned as OBSERVED and is discussed at its own test.
    ///     </para>
    /// </summary>
    public class CSharp14ConsumerShapeTests
    {
        private static (string Generated, IReadOnlyList<Diagnostic> Errors) Map(string source)
        {
            var (diags, gen) = GeneratorTestHarness.Run(source);
            return (gen, diags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList());
        }

        // ── `field`-backed properties (C# 14) ────────────────────────────────────────────────────────────
        // The backing field is compiler-synthesized, so the symbol is an ordinary IPropertySymbol with ordinary
        // accessors. That is exactly why this must be pinned rather than assumed: it is invisible in the symbol
        // model, so nothing would announce it if MemberFacts ever started reasoning about backing fields.

        [Fact]
        public void Field_backed_property_on_the_source_is_read()
        {
            var (gen, errors) = Map("""
                                    using DwarfMapper;
                                    namespace Demo;
                                    public class S { public string Name { get; set => field = value ?? ""; } = ""; }
                                    public class D { public string Name { get; set; } = ""; }
                                    [DwarfMapper] public partial class M { public partial D Map(S s); }
                                    """);

            Assert.Empty(errors);
            Assert.Contains("Name = s.Name", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void Field_backed_property_on_the_destination_is_written()
        {
            var (gen, errors) = Map("""
                                    using DwarfMapper;
                                    namespace Demo;
                                    public class S { public string Name { get; set; } = ""; }
                                    public class D { public string Name { get; set => field = value ?? ""; } = ""; }
                                    [DwarfMapper] public partial class M { public partial D Map(S s); }
                                    """);

            Assert.Empty(errors);
            Assert.Contains("Name = s.Name", gen, StringComparison.Ordinal);
        }

        // ── partial constructors (C# 14) ─────────────────────────────────────────────────────────────────
        // A partial constructor has a defining declaration and a separate implementing one. ConstructorSelector
        // ranks candidates and binds parameter names, so it has to see ONE constructor here, not two — and it
        // must not prefer the object-initializer path just because the declaration it happened to look at has no
        // body.
        [Fact]
        public void Partial_constructor_is_selected_and_bound_by_parameter_name()
        {
            var (gen, errors) = Map("""
                                    using DwarfMapper;
                                    namespace Demo;
                                    public class S { public int Id { get; set; } }
                                    public partial class D
                                    {
                                        public partial D(int id);
                                        public int Id { get; }
                                    }
                                    public partial class D { public partial D(int id) { Id = id; } }
                                    [DwarfMapper] public partial class M { public partial D Map(S s); }
                                    """);

            Assert.Empty(errors);
            Assert.Contains("new global::Demo.D(", gen, StringComparison.Ordinal);
            Assert.Contains("id: s.Id", gen, StringComparison.Ordinal);
        }

        // ── extension members (C# 14) ────────────────────────────────────────────────────────────────────
        // An extension block in the compilation must not perturb converter discovery. The pair below needs an
        // int -> string conversion, and the correct answer is the built-in formatter, NOT the extension member —
        // the user did not ask for it.
        [Fact]
        public void An_extension_block_in_scope_does_not_disturb_converter_discovery()
        {
            var (gen, errors) = Map("""
                                    using DwarfMapper;
                                    namespace Demo;
                                    public class S { public int V { get; set; } }
                                    public class D { public string V { get; set; } = ""; }
                                    public static class Ext { extension(int i) { public string Spell() => "n" + i; } }
                                    [DwarfMapper] public partial class M { public partial D Map(S s); }
                                    """);

            Assert.Empty(errors);
            Assert.Contains("V = __DwarfMap_FmtToStr", gen, StringComparison.Ordinal);
            Assert.DoesNotContain("Spell", gen, StringComparison.Ordinal);
        }

        // ── user-defined compound assignment (C# 14) ─────────────────────────────────────────────────────
        // `operator +=` declared on a mapped member's type adds 18 new WellKnownMemberNames to Roslyn 5.0. It is
        // not a conversion and must not be mistaken for one: the member is same-type and copies straight across.
        [Fact]
        public void A_user_defined_compound_assignment_operator_is_not_mistaken_for_a_conversion()
        {
            var (gen, errors) = Map("""
                                    using DwarfMapper;
                                    namespace Demo;
                                    public struct Money { public int C; public void operator +=(Money o) => C += o.C; }
                                    public class S { public Money V { get; set; } }
                                    public class D { public Money V { get; set; } }
                                    [DwarfMapper] public partial class M { public partial D Map(S s); }
                                    """);

            Assert.Empty(errors);
            Assert.Contains("V = s.V", gen, StringComparison.Ordinal);
        }

        // ── The one gap, pinned as OBSERVED ──────────────────────────────────────────────────────────────
        // `Use=` naming an extension member is refused with DWARF014 "Conversion method not found". The refusal
        // is loud and safe — no silent wrong mapping — but the reason is wrong: the method is plainly there, the
        // user is looking straight at it, and they are told it does not exist.
        //
        // This is the same shape as the DWARF028 case in the projection resolver, where "no matching source
        // member" was replaced by a message naming the real reason. Roslyn 5.0 supplies exactly what a better
        // message needs (INamedTypeSymbol.IsExtension / ExtensionParameter), so this is actionable — but whether
        // to teach `Use=` to RESOLVE extension members or merely to EXPLAIN why it will not is a design decision,
        // not a defect fix.
        //
        // Pinned as observed so that decision is made deliberately: if someone implements resolution, this test
        // fails and forces the choice to be recorded rather than drifting in.
        [Fact]
        public void Use_naming_an_extension_member_is_refused_with_DWARF014_for_now()
        {
            var (_, errors) = Map("""
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class S { public int V { get; set; } }
                                  public class D { public string V { get; set; } = ""; }
                                  public static class Ext { extension(int i) { public string Spell() => "n" + i; } }
                                  [DwarfMapper] public partial class M
                                  {
                                      [MapProperty(nameof(S.V), nameof(D.V), Use = nameof(Ext.Spell))]
                                      public partial D Map(S s);
                                  }
                                  """);

            Assert.Contains(errors, d => d.Id == "DWARF014");
        }
    }
}
