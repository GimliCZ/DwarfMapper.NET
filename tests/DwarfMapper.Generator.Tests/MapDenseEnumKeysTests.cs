// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>[MapDenseEnumKeys]</c>: the emitted fill, and — the part that matters — every refusal.
    ///     <para>
    ///         The emission is one line. Every risk in this feature is in the proof, so most of what is below
    ///         asserts that a shape whose indices cannot be bounded is REFUSED rather than mapped, and that the
    ///         refusal names the member the consumer has to go and look at.
    ///     </para>
    /// </summary>
    public class MapDenseEnumKeysTests
    {
        /// <summary>The shapes most tests here map, so the assertions differ only in what they claim.</summary>
        private const string Shapes = """
                                      using DwarfMapper;
                                      using System.Collections.Generic;
                                      using System.Runtime.CompilerServices;
                                      namespace Demo;
                                      public enum Platform { Web = 0, Ios = 1, Android = 2 }
                                      [InlineArray(3)] public struct Counts3 { private int _e0; }
                                      [InlineArray(4)] public struct Counts4 { private int _e0; }
                                      """;

        [Fact]
        public void An_enum_keyed_dictionary_fills_the_inline_array_by_index()
        {
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                            public sealed class B { public Counts3 Counts { get; set; } }
                                                            [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                            """);

            // The whole feature: a raw index, not a hash lookup, and not the dictionary helper.
            Assert.Contains("__DwarfDense_", gen, StringComparison.Ordinal);
            Assert.Contains("__r[(int)__i] = __kv.Value;", gen, StringComparison.Ordinal);
            Assert.DoesNotContain("__DwarfMapDict", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void The_index_is_computed_in_a_width_that_cannot_wrap_and_is_range_checked()
        {
            // The proof bounds the values the enum DECLARES. A dictionary can hold (Platform)999, and for an
            // enum wider than int a bare (int) cast of such a key WRAPS into range — measured:
            // (int)(E)0x1_0000_0001 is 1 — so the write would land in another key's slot with nothing thrown.
            // The index is therefore computed in long and tested as an unsigned quantity, which catches both
            // ends with one comparison.
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                            public sealed class B { public Counts3 Counts { get; set; } }
                                                            [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                            """);

            Assert.Contains("var __i = unchecked((long)__kv.Key - 0L);", gen, StringComparison.Ordinal);
            Assert.Contains("if (unchecked((ulong)__i) >= 3UL)", gen, StringComparison.Ordinal);
            Assert.Contains("global::System.ArgumentOutOfRangeException", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void The_last_slot_is_accepted_and_one_past_it_is_refused()
        {
            // Off-by-one is the whole failure mode of this feature, so both sides of the boundary are pinned.
            GeneratorAssert.CompilesClean(Shapes + """
                                                   public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                   public sealed class B { public Counts3 Counts { get; set; } }
                                                   [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                   """);

            var refused = GeneratorAssert.Reports("""
                                                  using DwarfMapper;
                                                  using System.Collections.Generic;
                                                  using System.Runtime.CompilerServices;
                                                  namespace Demo;
                                                  public enum Platform { Web = 0, Ios = 1, Android = 2, Desktop = 3 }
                                                  [InlineArray(3)] public struct Counts3 { private int _e0; }
                                                  public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                  public sealed class B { public Counts3 Counts { get; set; } }
                                                  [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                  """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("Demo.Platform.Desktop", StringComparison.Ordinal));
            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("[0, 3)", StringComparison.Ordinal));
        }

        [Fact]
        public void An_enum_extended_later_fails_the_build_rather_than_indexing_past_the_array()
        {
            // The feature's safety argument, asserted rather than trusted: the proof re-runs on every compile,
            // so a member added to the enum AFTER the mapper was written is a build error at the consumer's
            // next build — not a write outside the array at their next run. The pair below is byte-for-byte
            // the accepted fixture with one member appended.
            const string extended = """
                                    using DwarfMapper;
                                    using System.Collections.Generic;
                                    using System.Runtime.CompilerServices;
                                    namespace Demo;
                                    public enum Platform { Web = 0, Ios = 1, Android = 2, Watch = 3 }
                                    [InlineArray(3)] public struct Counts3 { private int _e0; }
                                    public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                    public sealed class B { public Counts3 Counts { get; set; } }
                                    [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                    """;

            var refused = GeneratorAssert.Reports(extended, "DWARF105");
            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("Demo.Platform.Watch", StringComparison.Ordinal));
            Assert.All(refused, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        }

        [Fact]
        public void A_negative_member_is_refused_because_it_indexes_before_the_array()
        {
            // The lower bound is not decoration: (int)kv.Key - offset is an INDEX, and a negative member points
            // at memory in front of the array.
            var refused = GeneratorAssert.Reports("""
                                                  using DwarfMapper;
                                                  using System.Collections.Generic;
                                                  using System.Runtime.CompilerServices;
                                                  namespace Demo;
                                                  public enum Platform { Unknown = -1, Web = 0, Ios = 1 }
                                                  [InlineArray(3)] public struct Counts3 { private int _e0; }
                                                  public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                  public sealed class B { public Counts3 Counts { get; set; } }
                                                  [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                  """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("Demo.Platform.Unknown", StringComparison.Ordinal));
        }

        [Fact]
        public void Offset_moves_the_window_and_is_proven_at_both_ends()
        {
            // A 1-based enum — the common shape, where 0 is reserved for "unset" — fits an array of exactly its
            // member count once Offset = 1 makes the first member slot 0.
            var gen = GeneratorAssert.CompilesClean("""
                                                    using DwarfMapper;
                                                    using System.Collections.Generic;
                                                    using System.Runtime.CompilerServices;
                                                    namespace Demo;
                                                    public enum Platform { Web = 1, Ios = 2, Android = 3 }
                                                    [InlineArray(3)] public struct Counts3 { private int _e0; }
                                                    public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                    public sealed class B { public Counts3 Counts { get; set; } }
                                                    [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts", Offset = 1)] public partial B Map(A a); }
                                                    """);

            Assert.Contains("var __i = unchecked((long)__kv.Key - 1L);", gen, StringComparison.Ordinal);

            // And the same offset applied to a 0-based enum pushes its first member off the FRONT.
            var refused = GeneratorAssert.Reports(Shapes + """
                                                           public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                           public sealed class B { public Counts3 Counts { get; set; } }
                                                           [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts", Offset = 1)] public partial B Map(A a); }
                                                           """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("Demo.Platform.Web", StringComparison.Ordinal));
            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("[1, 4)", StringComparison.Ordinal));
        }

        [Fact]
        public void A_Flags_enum_is_refused_because_its_key_space_is_the_power_set()
        {
            // A combination is a legitimate dictionary key that no member declares: Read|Write = 3 is inside
            // [0, 4) and would write slot 3, which names nothing. Proving the declared members are in range
            // proves nothing about the keys the dictionary can hold, so the shape is refused outright.
            var refused = GeneratorAssert.Reports("""
                                                  using DwarfMapper;
                                                  using System.Collections.Generic;
                                                  using System.Runtime.CompilerServices;
                                                  namespace Demo;
                                                  [System.Flags] public enum Access { None = 0, Read = 1, Write = 2 }
                                                  [InlineArray(4)] public struct Counts4 { private int _e0; }
                                                  public sealed class A { public Dictionary<Access, int> Counts { get; set; } }
                                                  public sealed class B { public Counts4 Counts { get; set; } }
                                                  [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                  """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("POWER SET", StringComparison.Ordinal));
        }

        [Fact]
        public void An_alias_member_neither_duplicates_a_slot_nor_widens_the_range()
        {
            // Two names for one value is legal and common. It cannot produce two dictionary entries, and the
            // proof judges VALUES rather than names — confirmed here rather than reasoned about.
            var gen = GeneratorAssert.CompilesClean("""
                                                    using DwarfMapper;
                                                    using System.Collections.Generic;
                                                    using System.Runtime.CompilerServices;
                                                    namespace Demo;
                                                    public enum Platform { None = 0, Default = 0, Ios = 1, Android = 2 }
                                                    [InlineArray(3)] public struct Counts3 { private int _e0; }
                                                    public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                    public sealed class B { public Counts3 Counts { get; set; } }
                                                    [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                    """);

            Assert.Contains("__DwarfDense_", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_byte_backed_enum_is_proven_the_same_way()
        {
            var gen = GeneratorAssert.CompilesClean("""
                                                    using DwarfMapper;
                                                    using System.Collections.Generic;
                                                    using System.Runtime.CompilerServices;
                                                    namespace Demo;
                                                    public enum Platform : byte { Web = 0, Ios = 1, Android = 2 }
                                                    [InlineArray(3)] public struct Counts3 { private int _e0; }
                                                    public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                    public sealed class B { public Counts3 Counts { get; set; } }
                                                    [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                    """);

            Assert.Contains("__DwarfDense_", gen, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("long")]
        [InlineData("ulong")]
        [InlineData("uint")]
        [InlineData("short")]
        [InlineData("sbyte")]
        public void An_enum_of_every_underlying_width_is_proven_and_compiles(string underlying)
        {
            // The refusal tests below cover the wide types that must NOT be accepted. This covers the other
            // half, which is the half the runtime guard exists for: a wide enum whose members ARE in range is
            // accepted, and the emitted `(long)__kv.Key` / `unchecked((ulong)__i)` must compile against every
            // legal underlying type rather than only against int.
            var gen = GeneratorAssert.CompilesClean($$"""
                                                      using DwarfMapper;
                                                      using System.Collections.Generic;
                                                      using System.Runtime.CompilerServices;
                                                      namespace Demo;
                                                      public enum Platform : {{underlying}} { Web = 0, Ios = 1, Android = 2 }
                                                      [InlineArray(3)] public struct Counts3 { private int _e0; }
                                                      public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                      public sealed class B { public Counts3 Counts { get; set; } }
                                                      [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                      """);

            Assert.Contains("var __i = unchecked((long)__kv.Key - 0L);", gen, StringComparison.Ordinal);
            Assert.Contains("if (unchecked((ulong)__i) >= 3UL)", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_nullable_annotated_source_dictionary_needs_no_null_forgiveness_at_the_call_site()
        {
            // The helper's parameter is NULLABLE and the helper answers null itself, on the collection and
            // dictionary helpers' precedent. A non-nullable parameter here would be CS8604 inside the .g.cs —
            // an unsuppressible warning in a file the consumer cannot edit — because `__DwarfDense_` does not
            // carry the `__DwarfMap_` prefix that would make the emitter append a `!`.
            var gen = GeneratorAssert.CompilesClean("""
                                                    using DwarfMapper;
                                                    using System.Collections.Generic;
                                                    using System.Runtime.CompilerServices;
                                                    namespace Demo;
                                                    public enum Platform { Web = 0, Ios = 1, Android = 2 }
                                                    [InlineArray(3)] public struct Counts3 { private int _e0; }
                                                    public sealed class A { public Dictionary<Platform, int>? Counts { get; set; } }
                                                    public sealed class B { public Counts3 Counts { get; set; } }
                                                    [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                    """,
                NullableContextOptions.Enable);

            Assert.Contains("global::System.Collections.Generic.Dictionary<global::Demo.Platform, int>? src",
                gen,
                StringComparison.Ordinal);
            Assert.DoesNotContain("(a.Counts!)", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_nullable_reference_value_into_a_non_nullable_slot_is_refused()
        {
            // CS8601 inside a .g.cs cannot be pragma'd or editorconfig'd by the consumer, and unlike a member
            // assignment there is no DWARF070 pathway behind this one — so forgiving it would store a null with
            // nothing said anywhere. Refused instead.
            var refused = GeneratorAssert.Reports("""
                                                  using DwarfMapper;
                                                  using System.Collections.Generic;
                                                  using System.Runtime.CompilerServices;
                                                  namespace Demo;
                                                  public enum Platform { Web = 0, Ios = 1, Android = 2 }
                                                  [InlineArray(3)] public struct Names3 { private string _e0; }
                                                  public sealed class A { public Dictionary<Platform, string?> Names { get; set; } = new(); }
                                                  public sealed class B { public Names3 Names { get; set; } }
                                                  [DwarfMapper] public partial class M { [MapDenseEnumKeys("Names")] public partial B Map(A a); }
                                                  """,
                "DWARF105",
                NullableContextOptions.Enable);

            Assert.Contains(refused,
                d => d.GetMessage(CultureInfo.InvariantCulture).Contains("which forbids null", StringComparison.Ordinal));
        }

        [Fact]
        public void A_long_backed_member_outside_int_is_refused_rather_than_wrapped_into_range()
        {
            // 0x1_0000_0001 casts to 1 in int — inside a 3-slot array, and therefore a WRONG-SLOT write rather
            // than an exception. The proof works in long, so the member is out of range and refused.
            var refused = GeneratorAssert.Reports("""
                                                  using DwarfMapper;
                                                  using System.Collections.Generic;
                                                  using System.Runtime.CompilerServices;
                                                  namespace Demo;
                                                  public enum Platform : long { Web = 0, Ios = 1, Far = 4294967297 }
                                                  [InlineArray(3)] public struct Counts3 { private int _e0; }
                                                  public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                  public sealed class B { public Counts3 Counts { get; set; } }
                                                  [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                  """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("4294967297", StringComparison.Ordinal));
        }

        [Fact]
        public void A_ulong_member_above_long_MaxValue_is_refused()
        {
            // A value with no long representation at all. Converting it would either throw or wrap into a
            // NEGATIVE number a range test could read as in-bounds, so it is reported out of range instead.
            var refused = GeneratorAssert.Reports("""
                                                  using DwarfMapper;
                                                  using System.Collections.Generic;
                                                  using System.Runtime.CompilerServices;
                                                  namespace Demo;
                                                  public enum Platform : ulong { Web = 0, Ios = 1, Huge = 18446744073709551615 }
                                                  [InlineArray(3)] public struct Counts3 { private int _e0; }
                                                  public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                  public sealed class B { public Counts3 Counts { get; set; } }
                                                  [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                  """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("18446744073709551615", StringComparison.Ordinal));
        }

        [Fact]
        public void A_destination_that_is_not_an_inline_array_is_refused()
        {
            var refused = GeneratorAssert.Reports(Shapes + """
                                                           public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                           public sealed class B { public int[] Counts { get; set; } }
                                                           [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                           """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("[InlineArray(n)]", StringComparison.Ordinal));
        }

        [Fact]
        public void An_int_keyed_dictionary_is_refused_because_it_declares_no_value_set()
        {
            var refused = GeneratorAssert.Reports(Shapes + """
                                                           public sealed class A { public Dictionary<int, int> Counts { get; set; } }
                                                           public sealed class B { public Counts3 Counts { get; set; } }
                                                           [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                           """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("is not an enum", StringComparison.Ordinal));
        }

        [Fact]
        public void A_source_that_is_not_a_dictionary_is_refused()
        {
            var refused = GeneratorAssert.Reports(Shapes + """
                                                           public sealed class A { public List<int> Counts { get; set; } }
                                                           public sealed class B { public Counts3 Counts { get; set; } }
                                                           [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                           """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("not a dictionary", StringComparison.Ordinal));
        }

        [Fact]
        public void A_value_type_that_does_not_match_the_slot_type_is_refused()
        {
            // The dense path assigns the value straight into the slot and performs no conversion, so a widening
            // that every other member mapping would do silently is refused here rather than emitted.
            var refused = GeneratorAssert.Reports(Shapes + """
                                                           public sealed class A { public Dictionary<Platform, long> Counts { get; set; } }
                                                           public sealed class B { public Counts3 Counts { get; set; } }
                                                           [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                           """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("performs no conversion", StringComparison.Ordinal));
        }

        [Fact]
        public void A_name_matching_no_writable_destination_member_is_refused()
        {
            var refused = GeneratorAssert.Reports(Shapes + """
                                                           public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                           public sealed class B { public Counts3 Counts { get; set; } }
                                                           [DwarfMapper] public partial class M { [MapDenseEnumKeys("Typo")] public partial B Map(A a); }
                                                           """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("'Typo'", StringComparison.Ordinal));
        }

        [Fact]
        public void Two_applications_naming_one_member_are_refused_rather_than_resolved_by_order()
        {
            // Two offsets are two different index computations. Picking one by declaration order would emit
            // arithmetic the consumer never chose, and which one they got would depend on attribute order.
            var refused = GeneratorAssert.Reports(Shapes + """
                                                           public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                           public sealed class B { public Counts4 Counts { get; set; } }
                                                           [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] [MapDenseEnumKeys("Counts", Offset = 1)] public partial B Map(A a); }
                                                           """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("more than once", StringComparison.Ordinal));
        }

        [Fact]
        public void The_directive_beside_a_MapValue_on_the_same_member_is_refused()
        {
            var refused = GeneratorAssert.Reports(Shapes + """
                                                           public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                           public sealed class B { public Counts3 Counts { get; set; } }
                                                           [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] [MapValue("Counts", Use = nameof(Fill))] public partial B Map(A a); private static Counts3 Fill() => default; }
                                                           """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("[MapValue]", StringComparison.Ordinal));
        }

        [Fact]
        public void The_directive_beside_a_value_transforming_MapProperty_is_refused_rather_than_ignored()
        {
            // Standing aside for the modifier is right; standing aside SILENTLY is the other half of the same
            // mistake, and it is the defect that shipped one directive over in T3.1.
            var refused = GeneratorAssert.Reports(Shapes + """
                                                           public sealed class A { public Dictionary<Platform, int> Raw { get; set; } }
                                                           public sealed class B { public Counts3 Counts { get; set; } }
                                                           [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] [MapProperty("Raw", "Counts", When = nameof(Yes))] public partial B Map(A a); private static bool Yes(A a) => true; }
                                                           """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("When=", StringComparison.Ordinal));
        }

        [Fact]
        public void A_MapProperty_rename_still_takes_the_dense_path()
        {
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public Dictionary<Platform, int> Raw { get; set; } }
                                                            public sealed class B { public Counts3 Counts { get; set; } }
                                                            [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] [MapProperty("Raw", "Counts")] public partial B Map(A a); }
                                                            """);

            Assert.Contains("__DwarfDense_", gen, StringComparison.Ordinal);
            Assert.Contains("Counts = __DwarfDense_", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void Update_into_takes_the_dense_path_too()
        {
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                            public sealed class B { public Counts3 Counts { get; set; } }
                                                            [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts")] public partial void Map(A a, B b); }
                                                            """);

            Assert.Contains("__DwarfDense_", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_directive_naming_a_member_resolution_never_reached_is_reported_rather_than_silent()
        {
            // The member exists and is writable, but a constructor parameter claims it before auto-matching sees
            // it — so the directive is not in force and the dictionary the consumer believes is being indexed
            // densely is not. "Accepted it, changed nothing, said nothing" is the shape this round removes.
            var refused = GeneratorAssert.Reports(Shapes + """
                                                           public sealed class A { public int Id { get; set; } }
                                                           public sealed class B { public B(int id) { Id = id; } public int Id { get; set; } }
                                                           [DwarfMapper] public partial class M { [MapDenseEnumKeys("Id")] public partial B Map(A a); }
                                                           """,
                "DWARF105");

            Assert.Contains(refused, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("never reached", StringComparison.Ordinal));
        }

        [Fact]
        public void The_directive_on_an_endpoint_that_cannot_emit_a_loop_is_reported_rather_than_dropped()
        {
            // DWARF092. A projection is translated into an expression tree, which has no statement for the fill
            // loop to live in — so the directive is discarded there. Discarding it SILENTLY is the failure:
            // a dictionary into an inline array is no conversion at all, so the caller would meet the ordinary
            // refusal for an unmappable member with nothing saying the directive they wrote was not in force.
            var reported = GeneratorAssert.Reports("""
                                                   using DwarfMapper;
                                                   using System.Linq;
                                                   using System.Collections.Generic;
                                                   using System.Runtime.CompilerServices;
                                                   namespace Demo;
                                                   public enum Platform { Web = 0, Ios = 1, Android = 2 }
                                                   [InlineArray(3)] public struct Counts3 { private int _e0; }
                                                   public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                   public sealed class B { public Counts3 Counts { get; set; } }
                                                   [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts", Offset = 1)] public partial IQueryable<B> Project(IQueryable<A> q); }
                                                   """,
                "DWARF092");

            // Quoted in the form the caller wrote it, Offset included — the message is text they can move.
            Assert.Contains(reported,
                d => d.GetMessage(CultureInfo.InvariantCulture)
                    .Contains("[MapDenseEnumKeys(\"Counts\", Offset = 1)]", StringComparison.Ordinal));
        }

        [Fact]
        public void Two_methods_naming_the_same_unreached_member_are_both_reported()
        {
            // The post-pass reads the diagnostics list back so it does not raise a second, vaguer refusal about
            // a member it already refused precisely — and that list belongs to the mapper CLASS, not to one
            // method. Scanned whole, one method's refusal silenced every other method that named the same
            // member: two independent questions with one answer between them. The scan starts at this
            // resolution's own first entry instead.
            var refused = GeneratorAssert.Reports(Shapes + """
                                                           public sealed class A { public int Id { get; set; } }
                                                           public sealed class B { public B(int id) { Id = id; } public int Id { get; set; } }
                                                           [DwarfMapper] public partial class M {
                                                               [MapDenseEnumKeys("Id")] public partial B One(A a);
                                                               [MapDenseEnumKeys("Id")] public partial B Two(A a);
                                                           }
                                                           """,
                "DWARF105");

            Assert.Equal(2,
                refused.Count(d => d.GetMessage(CultureInfo.InvariantCulture)
                    .Contains("never reached", StringComparison.Ordinal)));
        }

        [Fact]
        public void Nothing_is_dense_without_the_directive()
        {
            // The trigger is the attribute, not the shape: whether a member should BE an inline array is a
            // declaration the consumer writes, and nothing here is decided for them.
            GeneratorAssert.DoesNotReport(Shapes + """
                                                   public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                                                   public sealed class B { public Counts3 Counts { get; set; } }
                                                   [DwarfMapper] public partial class M { [MapIgnore("Counts")] public partial B Map(A a); }
                                                   """,
                "DWARF105");
        }
    }
}
