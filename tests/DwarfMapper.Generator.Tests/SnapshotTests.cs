// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

public class SnapshotTests
{
    [Fact]
    public Task Flat_mapper_output_is_stable()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Person { public int Age { get; set; } public string Name { get; set; } = ""; }
                           public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class PersonMapper { public partial PersonDto ToDto(Person p); }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verify(generated);
    }
}
