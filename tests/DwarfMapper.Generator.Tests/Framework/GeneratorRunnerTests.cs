// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Registry;

namespace DwarfMapper.Generator.Tests.Framework;

public class GeneratorRunnerTests
{
    private const string MapperSource = """
                                        using DwarfMapper;
                                        namespace Demo;
                                        public class A { public int X { get; set; } }
                                        public class B { public int X { get; set; } }
                                        [DwarfMapper] public partial class M { public partial B Map(A a); }
                                        """;

    [Fact]
    public void Runs_the_class_model_generator_and_returns_outputs_by_hint_name()
    {
        var run = GeneratorRunner.Run(new DwarfGenerator(), MapperSource);

        Assert.NotEmpty(run.OutputsByHintName);
        Assert.Contains(run.OutputsByHintName, kv => kv.Value.Contains("partial class M", StringComparison.Ordinal));
    }

    [Fact]
    public void Returns_the_assembly_wide_aggregate_outputs_too()
    {
        // The existing GeneratorTestHarness.Run FILTERS these out so per-mapper snapshots pick the right file.
        // The golden corpus depends on them being present, or the facade/DI/manifest emitters are never covered.
        var run = GeneratorRunner.Run(new DwarfGenerator(), MapperSource);

        Assert.Contains(run.OutputsByHintName.Keys,
            k => k.EndsWith("DwarfMapper.Extensions.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Runs_the_registry_generator_with_no_bespoke_method()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           [MapTo(typeof(Dto))] public class Src { public int Id { get; set; } }
                           public class Dto { public int Id { get; set; } }
                           """;

        var run = GeneratorRunner.Run(new MapToGenerator(), src);

        Assert.Contains(run.OutputsByHintName, kv => kv.Value.Contains("ToDto", StringComparison.Ordinal));
    }
}
