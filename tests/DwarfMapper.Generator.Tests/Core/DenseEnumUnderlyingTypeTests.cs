// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Core
{
    /// <summary>
    ///     <c>[MapDenseEnumKeys]</c> across every underlying type an enum may declare.
    ///     <para>
    ///         The dense proof reads each member's constant through a switch with one arm per underlying type.
    ///         The corpus declares its enums with the default <c>int</c>, so seven of the eight arms were
    ///         unreached — the round-29 Codecov patch report flagged the <c>ushort</c> pair among them.
    ///     </para>
    ///     <para>
    ///         This is not box-ticking: the arms exist because a member's <c>ConstantValue</c> is boxed as its
    ///         underlying CLR type, and an arm that read the wrong one would compute the wrong slot index. The
    ///         dense fill writes by index, so a wrong index writes a value into the slot belonging to a
    ///         different member — silently, since every slot is the same type.
    ///     </para>
    /// </summary>
    public class DenseEnumUnderlyingTypeTests
    {
        private static string Source(string underlying, int slots)
        {
            return $$"""
                     using System.Collections.Generic;
                     using System.Runtime.CompilerServices;
                     using DwarfMapper;
                     namespace Demo;

                     public enum Ore : {{underlying}} { Iron = 0, Coal = 1, Gold = 2, Mithril = 3 }

                     [InlineArray({{slots}})]
                     public struct Slots { private int _e0; }

                     public sealed class Src { public Dictionary<Ore, int> Yield { get; set; } = new(); }
                     public sealed class Dst { public Slots Yield { get; set; } }

                     [DwarfMapper]
                     public partial class M
                     {
                         [MapDenseEnumKeys(nameof(Dst.Yield))]
                         public partial Dst Map(Src s);
                     }
                     """;
        }

        /// <summary>
        ///     Every underlying type C# permits for an enum. Each must produce the dense fill, and the emitted
        ///     switch must carry one case per declared member — four here — because a member the proof failed to
        ///     read is a member whose slot is never written.
        /// </summary>
        [Theory]
        [InlineData("byte")]
        [InlineData("sbyte")]
        [InlineData("short")]
        [InlineData("ushort")]
        [InlineData("int")]
        [InlineData("uint")]
        [InlineData("long")]
        [InlineData("ulong")]
        public void Every_enum_underlying_type_reaches_the_dense_fill(string underlying)
        {
            var g = GeneratorAssert.CompilesClean(Source(underlying, 4), NullableContextOptions.Enable);

            // One case per declared member. Counting them is what catches an arm that read the constant
            // wrongly and collapsed two members onto one slot.
            var cases = System.Text.RegularExpressions.Regex.Matches(g, @"case global::Demo\.Ore\.\w+:").Count;
            Assert.True(cases == 4,
                $"expected 4 switch cases for the four declared members on an enum : {underlying}, found {cases}.\n{g}");
        }

        /// <summary>
        ///     The control. The assertion above counts cases in generated text, so a source that silently stopped
        ///     producing a dense fill at all would report zero and read like a proof failure rather than an
        ///     absent feature. This pins that the SAME shape without the directive emits no switch, so the count
        ///     above is attributable to <c>[MapDenseEnumKeys]</c>.
        /// </summary>
        [Fact]
        public void Without_the_directive_there_is_no_dense_switch()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public enum Ore { Iron = 0, Coal = 1 }
                               public sealed class Src { public Dictionary<Ore, int> Yield { get; set; } = new(); }
                               public sealed class Dst { public Dictionary<Ore, int> Yield { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class M { public partial Dst Map(Src s); }
                               """;

            var g = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);

            Assert.DoesNotContain("case global::Demo.Ore.", g, StringComparison.Ordinal);
        }
    }
}
