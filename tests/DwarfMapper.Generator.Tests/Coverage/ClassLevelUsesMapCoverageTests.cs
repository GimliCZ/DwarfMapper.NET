// SPDX-License-Identifier: GPL-2.0-only

// Coverage suite for DwarfGenerator's ambient REQUIRES manifest. It merges four sources of consumed pairs: facade
// Map calls, assembly-level [UsesMap], and class-level [UsesMap] in both its generic and its typeof forms. Only the
// generic class-level form had a fixture. A class carrying the typeof form contributed nothing the tests could see,
// so that merge loop never ran, and a pair declared that way must reach the manifest like the rest.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ClassLevelUsesMapCoverageTests
    {
        [Fact]
        public void A_class_level_typeof_UsesMap_is_written_to_the_requires_manifest()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Doc { }
                                  public class Model { }
                                  [UsesMap(typeof(Doc), typeof(Model))]
                                  public class Consumer { }
                                  """;

            var manifest = GeneratorTestHarness.RunAndGetSource(source, "DwarfMapper.AmbientRequires.g.cs");

            Assert.Contains("[assembly: global::DwarfMapper.DwarfRequiresMap(typeof(global::Demo.Doc), typeof(global::Demo.Model))]", manifest, StringComparison.Ordinal);
        }
    }
}
