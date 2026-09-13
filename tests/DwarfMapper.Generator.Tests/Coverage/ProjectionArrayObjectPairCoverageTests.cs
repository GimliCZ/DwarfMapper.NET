// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Projection.cs's ResolveProjectionExpr nested-object gate. It asks for a named
// source AND a named target before it tries a nested member-init. An array on either side is not an object pair, and no
// collection projection claims an array paired with a plain class, so the member is refused as untranslatable
// (DWARF028) rather than projected as a nested object.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ProjectionArrayObjectPairCoverageTests
    {
        private static void AssertUntranslatable(string types)
        {
            var source = "using DwarfMapper;\nusing System.Linq;\nnamespace Demo;\n" + types +
                         "\n[DwarfMapper]\npublic partial class M { public partial IQueryable<D> Project(IQueryable<S> src); }\n";

            var d = Assert.Single(GeneratorTestHarness.Run(source, NullableContextOptions.Enable).Diagnostics, x => x.Id == "DWARF028");

            Assert.Equal("Projection member 'I' cannot be translated to SQL: no translatable conversion found; map at runtime instead.", d.GetMessage(CultureInfo.InvariantCulture));
        }

        [Fact]
        public void An_object_source_into_an_array_target_is_untranslatable()
        {
            AssertUntranslatable("""
                                 public class Inner { public int A { get; set; } }
                                 public class S { public Inner I { get; set; } = new(); }
                                 public class D { public int[] I { get; set; } = new int[0]; }
                                 """);
        }

        [Fact]
        public void An_array_source_into_an_object_target_is_untranslatable()
        {
            AssertUntranslatable("""
                                 public class InnerDto { public int A { get; set; } }
                                 public class S { public int[] I { get; set; } = new int[0]; }
                                 public class D { public InnerDto? I { get; set; } }
                                 """);
        }
    }
}
