// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage suite for MapperExtractor.Phases.cs arms that only a nested pair's construction or an update-into
// target's shape reaches:
//   - DrainNestedMappingQueue's factory lookup: a pair-scoped [MapConstructor] for a DIFFERENT pair is skipped, and
//     one whose factory does not take the source type leaves the nested side without a factory (DWARF059 on the
//     declared pair);
//   - DrainNestedMappingQueue's ctor-argument resolution failing for the nested pair (DWARF024);
//   - update-into's init-only scan walking an interface target (no base type) and a base class carrying both an
//     init-only and a get-only property (DWARF007 for each).
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class NestedPairAndUpdateTargetCoverageTests
    {
        private static string Message(string src, string id, string mustContain) =>
            GeneratorAssert.Reports(src, id)
                .Select(d => d.GetMessage(CultureInfo.InvariantCulture))
                .Single(m => m.Contains(mustContain, StringComparison.Ordinal));

        [Fact]
        public void Nested_declared_pair_skips_a_pair_constructor_for_another_pair_and_uses_its_own()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Inner { public int A { get; set; } }
                               public class InnerDto { public int A { get; set; } }
                               public class Other { public int B { get; set; } }
                               public class OtherDto { public int B { get; set; } }
                               public class Outer { public Inner I { get; set; } = new(); }
                               public class OuterDto { public InnerDto I { get; set; } = new(); }
                               [DwarfMapper(AutoNest = true, ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                               [GenerateMap<Outer, OuterDto>]
                               [GenerateMap<Inner, InnerDto>]
                               [GenerateMap<Other, OtherDto>]
                               [MapConstructor<Other, OtherDto>(nameof(MakeOther))]
                               [MapConstructor<Inner, InnerDto>(nameof(MakeInner))]
                               public partial class M
                               {
                                   private static OtherDto MakeOther(Other o) => new OtherDto();
                                   private static InnerDto MakeInner(Inner i) => new InnerDto();
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            // The Preserve helper for the nested Inner pair must build through the factory the declared pair uses,
            // not through MakeOther, whose attribute appears first.
            Assert.Contains("var __dwarf_t = MakeInner(s);", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("MakeOther(s)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Nested_declared_pair_with_a_factory_that_does_not_take_the_source_is_refused()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Inner { public int A { get; set; } }
                               public class InnerDto { public int A { get; set; } }
                               public class Other { public int B { get; set; } }
                               public class Outer { public Inner I { get; set; } = new(); }
                               public class OuterDto { public InnerDto I { get; set; } = new(); }
                               [DwarfMapper(AutoNest = true, ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                               [GenerateMap<Outer, OuterDto>]
                               [GenerateMap<Inner, InnerDto>]
                               [MapConstructor<Inner, InnerDto>(nameof(MakeInner))]
                               public partial class M
                               {
                                   private static InnerDto MakeInner(Other o) => new InnerDto();
                               }
                               """;

            var message = Assert.Single(GeneratorAssert.Reports(src, "DWARF059")).GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("\"MakeInner\"", message, StringComparison.Ordinal);
            Assert.Contains("'Demo.Inner'", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Nested_pair_whose_constructor_parameter_has_no_source_is_refused()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Inner { public int A { get; set; } }
                               public class InnerDto { public InnerDto(int missing) { A = missing; } public int A { get; } }
                               public class Outer { public Inner I { get; set; } = new(); }
                               public class OuterDto { public InnerDto? I { get; set; } }
                               [DwarfMapper(AutoNest = true)]
                               public partial class M { public partial OuterDto Map(Outer o); }
                               """;

            var message = Assert.Single(GeneratorAssert.Reports(src, "DWARF024")).GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("'missing'", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Update_into_an_interface_target_assigns_its_settable_members()
        {
            // An interface has no base type, so the init-only scan's base-type walk ends on null rather than on object.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } }
                               public interface IDst { int A { get; set; } }
                               [DwarfMapper]
                               public partial class M { public partial void Update(Src s, IDst d); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("d.A = s.A;", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Update_into_refuses_inherited_init_only_and_get_only_members()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } public int B { get; set; } public int C { get; set; } }
                               public class Base { public int A { get; init; } public int C { get; } }
                               public class Dst : Base { public int B { get; set; } }
                               [DwarfMapper]
                               public partial class M { public partial void Update(Src s, Dst d); }
                               """;

            Assert.Contains("[MapIgnore(\"A\")]", Message(src, "DWARF007", "'A'"), StringComparison.Ordinal);
            Assert.Contains("[MapIgnore(\"C\")]", Message(src, "DWARF007", "'C'"), StringComparison.Ordinal);
        }
    }
}
