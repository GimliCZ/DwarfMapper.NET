// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Projection.cs's ResolveProjectionMembers under IgnoreObsoleteMembers. An obsolete
// destination member is dropped from the projection unless a directive targets it explicitly. Opting a retired member
// back in must work through [MapValue] as well as [MapProperty], exactly as the create map reads it, or .Project
// would drop what .Map keeps.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ProjectionObsoleteExplicitTargetsCoverageTests
    {
        [Fact]
        public void Obsolete_members_targeted_by_MapProperty_or_MapValue_stay_in_the_projection()
        {
            const string source = """
                                  using DwarfMapper;
                                  using System.Linq;
                                  namespace Demo;
                                  public class S { public int A { get; set; } public int B { get; set; } }
                                  public class D { public int A { get; set; } [System.Obsolete] public int B { get; set; } [System.Obsolete] public int C { get; set; } }
                                  [DwarfMapper(IgnoreObsoleteMembers = true)]
                                  public partial class M
                                  {
                                      [MapProperty("B", "B")]
                                      [MapValue("C", 7)]
                                      public partial IQueryable<D> Project(IQueryable<S> src);
                                  }
                                  """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("B = __s.B,", generated, StringComparison.Ordinal);
            Assert.Contains("C = 7,", generated, StringComparison.Ordinal);
            Assert.Contains("A = __s.A,", generated, StringComparison.Ordinal);
        }
    }
}
