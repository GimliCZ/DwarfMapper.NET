// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Projection.cs's ResolveProjectionNestedObjectExpr stop on an undecided constructor.
// A nested projection target goes through the same constructor decision as a top-level one. When the selector reports
// a blocking ambiguity (DWARF025: two public constructors tie on the widest arity), the nested resolver stops there. It
// must not resolve members on top of a constructor it does not have, and it must not stack a second "untranslatable"
// report on the same member.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ProjectionNestedConstructorRefusalCoverageTests
    {
        [Fact]
        public void A_nested_target_with_tied_constructors_is_refused_once_as_ambiguous()
        {
            const string source = """
                                  using DwarfMapper;
                                  using System.Linq;
                                  namespace Demo;
                                  public class Inner { public int A { get; set; } }
                                  public class InnerDto { public InnerDto(int a) { A = a; } public InnerDto(long a) { A = (int)a; } public int A { get; } }
                                  public class S { public Inner I { get; set; } = new(); }
                                  public class D { public InnerDto? I { get; set; } }
                                  [DwarfMapper]
                                  public partial class M { public partial IQueryable<D> Project(IQueryable<S> src); }
                                  """;

            var diagnostics = GeneratorTestHarness.Run(source, NullableContextOptions.Enable).Diagnostics;

            var d = Assert.Single(diagnostics, x => x.Id == "DWARF025");
            Assert.StartsWith("Destination type 'InnerDto' has an ambiguous constructor selection", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.DoesNotContain(diagnostics, x => x.Id == "DWARF028");
        }
    }
}
