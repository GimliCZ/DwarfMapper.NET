// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     Research A6: no generator code names <c>MaybeNull</c>/<c>DisallowNull</c>, so a member whose ANNOTATION says
    ///     one thing and whose ATTRIBUTE says another might slip past DWARF070 — a null flowing into a destination that
    ///     forbids it, with nothing reported. Mapperly fixed exactly this pair in 5.0.0-next.9 (#2333, #2334). These
    ///     rows probe both directions; whichever way they land, they stay as the pin.
    /// </summary>
    public sealed class NullabilityAttributeProbeTests
    {
        [Fact]
        public void A_MaybeNull_source_into_a_non_nullable_destination_reports_DWARF070()
        {
            const string src = """
                #nullable enable
                using DwarfMapper;
                namespace T23a;
                public class S { [System.Diagnostics.CodeAnalysis.MaybeNull] public string Name { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; }
                [DwarfMapper] public partial class M { public partial D Map(S s); }
                """;
            AssertDwarf070(src);
        }

        [Fact]
        public void A_nullable_source_into_a_DisallowNull_destination_reports_DWARF070()
        {
            const string src = """
                #nullable enable
                using DwarfMapper;
                namespace T23b;
                public class S { public string? Name { get; set; } }
                public class D { [System.Diagnostics.CodeAnalysis.DisallowNull] public string? Name { get; set; } }
                [DwarfMapper] public partial class M { public partial D Map(S s); }
                """;
            AssertDwarf070(src);
        }

        [Fact] // control: the annotation-only shape the two rows above vary
        public void A_nullable_source_into_a_non_nullable_destination_reports_DWARF070()
        {
            const string src = """
                #nullable enable
                using DwarfMapper;
                namespace T23c;
                public class S { public string? Name { get; set; } }
                public class D { public string Name { get; set; } = ""; }
                [DwarfMapper] public partial class M { public partial D Map(S s); }
                """;
            AssertDwarf070(src);
        }

        private static void AssertDwarf070(string src)
        {
            // The other half of DWARF070's trade: the '!' it pairs with keeps CS8601 out of the .g.cs, where the
            // consumer could not suppress it.
            var leaked = GeneratorTestHarness.GeneratedCodeWarnings(src);
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.True(diagnostics.Any(d => string.Equals(d.Id, "DWARF070", StringComparison.Ordinal)) && leaked.IsEmpty,
                "expected DWARF070 and a warning-free .g.cs; got generator: [" +
                string.Join(", ", diagnostics.Select(d => d.Id)) + "], inside generated code: [" +
                string.Join(", ", leaked.Select(d => d.Id)) + "]");
        }
    }
}
