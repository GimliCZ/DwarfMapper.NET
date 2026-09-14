// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     DWARF012 — a destination member that is [MapIgnore]d AND named by a directive that maps it — must name
    ///     the directive the consumer actually wrote.
    /// </summary>
    /// <remarks>
    ///     The descriptor was written for [MapProperty] and later reused for three more directives that conflict
    ///     with an ignore the same way: [Reinterpret], [MapShare] and [MapDenseEnumKeys]. Its text still said
    ///     "mapped via [MapProperty]" for all four, so three of them sent the reader looking for an attribute
    ///     that is not on the method. Round 30 found it through the [MapDenseEnumKeys] case; the other two share
    ///     the wording and the fix.
    /// </remarks>
    public class IgnoreConflictDirectiveNameTests
    {
        private const string Dense = """
                                     using DwarfMapper;
                                     using System.Collections.Generic;
                                     using System.Runtime.CompilerServices;
                                     namespace Demo;
                                     public enum Platform { Web = 0, Ios = 1, Android = 2 }
                                     [InlineArray(3)] public struct Counts3 { private int _e0; }
                                     public sealed class A { public Dictionary<Platform, int> Counts { get; set; } = new(); }
                                     public sealed class B { public Counts3 Counts { get; set; } }
                                     [DwarfMapper] public partial class M { [MapIgnore("Counts")][MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                     """;

        private const string Property = """
                                        using DwarfMapper;
                                        namespace Demo;
                                        public class Source { public string Full { get; set; } = ""; }
                                        public class Target { public string Name { get; set; } = ""; }
                                        [DwarfMapper] public partial class M { [MapIgnore("Name")][MapProperty("Full", "Name")] public partial Target Map(Source s); }
                                        """;

        private const string Reinterpret = """
                                           using DwarfMapper;
                                           namespace Demo;
                                           public struct V { public int X; }
                                           public class C { public V[] Items { get; set; } = System.Array.Empty<V>(); }
                                           public class D { public V[] Items { get; set; } = System.Array.Empty<V>(); }
                                           [DwarfMapper] public partial class M { [Reinterpret("Items")][MapIgnore("Items")] public partial D Map(C c); }
                                           """;

        private const string Share = """
                                     using System.Collections.Immutable;
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class Src { public ImmutableArray<int> Items { get; set; } }
                                     public class Dst { public ImmutableArray<int> Items { get; set; } }
                                     [DwarfMapper] public partial class M { [MapShare("Items")][MapIgnore("Items")] public partial Dst Map(Src s); }
                                     """;

        public static TheoryData<string, string, string> Conflicts => new()
        {
            { Property, "Name", "[MapProperty]" },
            { Reinterpret, "Items", "[Reinterpret]" },
            { Share, "Items", "[MapShare]" },
            { Dense, "Counts", "[MapDenseEnumKeys]" }
        };

        [Theory]
        [MemberData(nameof(Conflicts))]
        public void The_conflict_names_the_directive_that_was_written(string source, string member, string directive)
        {
            var conflict = Assert.Single(GeneratorTestHarness.Run(source).Diagnostics, d => d.Id == "DWARF012");
            var message = conflict.GetMessage(CultureInfo.InvariantCulture);

            Assert.Equal($"Destination member '{member}' is both ignored via [MapIgnore] and mapped via {directive}; remove one",
                message);
            Assert.Equal("Conflicting [MapIgnore] and a mapping directive",
                conflict.Descriptor.Title.ToString(CultureInfo.InvariantCulture));
        }
    }
}
