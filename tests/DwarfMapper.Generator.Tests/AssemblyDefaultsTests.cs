// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// <c>[assembly: DwarfMapperDefaults(...)]</c> — one house style set once, inherited by every mapper that does
/// not override it. Precedence: <b>mapper &gt; assembly defaults &gt; built-in default</b>. Implemented by
/// layering the assembly-defaults attribute UNDER the mapper's own attribute, so these tests pin the precedence
/// at all three levels via observable behaviour (whether a case-differing member maps).
/// </summary>
public class AssemblyDefaultsTests
{
    private static string[] Ids(string source) =>
        GeneratorTestHarness.Run(source).Diagnostics.Select(d => d.Id).ToArray();

    // Src has 'Name', Dst has 'NAME' — they pair only under case-insensitive matching. (The assembly attribute
    // sits AFTER the using directive, as C# requires.)
    private const string CaseDiffering = """
        using DwarfMapper;
        {ASM}
        namespace Demo;
        public class Src { public int Name { get; set; } }
        public class Dst { public int NAME { get; set; } }
        [DwarfMapper{MAPPER}]
        public partial class M { public partial Dst Map(Src s); }
        """;

    private static string Build(string asm, string mapper) =>
        CaseDiffering.Replace("{ASM}", asm, System.StringComparison.Ordinal)
            .Replace("{MAPPER}", mapper, System.StringComparison.Ordinal);

    [Fact]
    public void Built_in_default_applies_when_neither_sets_the_option()
    {
        // No assembly default, no mapper option → case-sensitive (built-in) → NAME is unmapped.
        Assert.Contains("DWARF001", Ids(Build(asm: "", mapper: "")));
    }

    [Fact]
    public void Assembly_default_applies_when_the_mapper_does_not_override()
    {
        // Assembly default turns case-insensitivity on → Name pairs with NAME → no DWARF001.
        var ids = Ids(Build(
            asm: "[assembly: DwarfMapper.DwarfMapperDefaults(CaseInsensitive = true)]",
            mapper: ""));

        Assert.DoesNotContain("DWARF001", ids);
    }

    [Fact]
    public void Mapper_option_overrides_the_assembly_default()
    {
        // Assembly says case-insensitive, but the mapper explicitly turns it back off → NAME unmapped again.
        // This is the crucial precedence assertion: the mapper's own value wins.
        var ids = Ids(Build(
            asm: "[assembly: DwarfMapper.DwarfMapperDefaults(CaseInsensitive = true)]",
            mapper: "(CaseInsensitive = false)"));

        Assert.Contains("DWARF001", ids);
    }

    [Fact]
    public void Assembly_default_layers_a_second_independent_option()
    {
        // A different option (RequiredMapping = Both) set assembly-wide makes an unconsumed source surface
        // DWARF039, proving the layering is generic, not special-cased to CaseInsensitive.
        const string source = """
            using DwarfMapper;
            [assembly: DwarfMapper.DwarfMapperDefaults(RequiredMapping = RequiredMappingStrategy.Both)]
            namespace Demo;
            public class Src { public int A { get; set; } public int Unused { get; set; } }
            public class Dst { public int A { get; set; } }
            [DwarfMapper]
            public partial class M { public partial Dst Map(Src s); }
            """;

        Assert.Contains("DWARF039", Ids(source));
    }
}
