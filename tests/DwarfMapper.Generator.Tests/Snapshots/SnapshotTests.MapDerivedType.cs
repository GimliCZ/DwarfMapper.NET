// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    [Fact]
    public Task Snap_MapDerivedType_ThreeLevel()
    {
        // Freeze the most-derived-first switch arm ORDER for a 3-level hierarchy
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public abstract class Animal { public string Name { get; set; } = ""; }
            public class Dog    : Animal { public string Breed { get; set; } = ""; }
            public class Puppy  : Dog    { public bool IsVaccinated { get; set; } }
            public class AnimalDto { public string Name { get; set; } = ""; }
            public class DogDto    : AnimalDto { public string Breed { get; set; } = ""; }
            public class PuppyDto  : DogDto    { public bool IsVaccinated { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                [MapDerivedType<Puppy, PuppyDto>]
                [MapDerivedType<Dog,   DogDto>]
                public partial AnimalDto Map(Animal a);
                public partial PuppyDto Map(Puppy p);
                public partial DogDto   Map(Dog d);
            }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
