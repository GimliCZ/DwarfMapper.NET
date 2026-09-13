// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Projection.cs's ChooseProjectionConstructor fallback. When the selector's answer is
// a parameterless constructor ([DwarfMapperConstructor] on it) but member-init cannot carry the target, because every
// member is get-only, the projection falls back to the widest PUBLIC constructor. A private one is passed over however
// wide it is, and among the public ones the most parameters win.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ProjectionWidestConstructorFallbackCoverageTests
    {
        [Fact]
        public void An_annotated_parameterless_constructor_on_a_get_only_target_falls_back_to_the_widest_public_one()
        {
            const string source = """
                                  using DwarfMapper;
                                  using System.Linq;
                                  namespace Demo;
                                  public class S { public int A { get; set; } public int B { get; set; } }
                                  public class D
                                  {
                                      [DwarfMapperConstructor] public D() { }
                                      public D(int a) { A = a; }
                                      public D(int a, int b) { A = a; B = b; }
                                      private D(string s) { }
                                      public int A { get; }
                                      public int B { get; }
                                  }
                                  [DwarfMapper]
                                  public partial class M { public partial IQueryable<D> Project(IQueryable<S> src); }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("__s => new global::Demo.D(__s.A, __s.B));", generated, StringComparison.Ordinal);
        }
    }
}
