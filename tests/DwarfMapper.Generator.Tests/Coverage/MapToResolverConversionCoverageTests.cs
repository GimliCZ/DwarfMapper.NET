// SPDX-License-Identifier: GPL-2.0-only

// Coverage for the [MapTo] registry resolver's built-in conversion arms. After a direct assignment fails, the resolver
// tries a checked numeric conversion, then a string parse, then an enum conversion. None of those three arms had ever
// produced a member in the full suite — every registry fixture mapped identical or implicitly convertible types — so the
// registry's numeric, parsable and enum support was never shown to emit anything.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MapToResolverConversionCoverageTests
    {
        [Fact]
        public void Numeric_narrowing_string_parsing_and_enum_members_each_go_through_their_converter()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public enum KindA { One, Two }
                               public enum KindB { One, Two }
                               public class Dst { public int Count { get; set; } public int Age { get; set; } public KindB Kind { get; set; } }
                               [MapTo(typeof(Dst))] public class Src { public long Count { get; set; } public string Age { get; set; } = "1"; public KindA Kind { get; set; } }
                               """;

            var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(src);

            Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("DWARFR05", StringComparison.Ordinal));
            Assert.Contains("Count = __DwarfMap_Num_long__int_", generated, StringComparison.Ordinal);
            Assert.Contains("Age = __DwarfMap_StrParse_int_", generated, StringComparison.Ordinal);
            Assert.Contains("Kind = __DwarfMap_EnumName_global__Demo_KindA__global__Demo_KindB_", generated, StringComparison.Ordinal);
        }
    }
}
