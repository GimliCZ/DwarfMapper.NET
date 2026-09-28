// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     Research P5d: <c>Project(q)</c> takes the whole query, so a consumer could not write the EF-native
    ///     <c>db.Orders.Where(…).OrderBy(…).Select(OrderMapper.ProjectExpression)</c>. Each projection now exposes its
    ///     tree as a static <c>{Method}Expression</c> — the SAME instance the method applies — and DWARF112 explains
    ///     its absence where the name is taken.
    /// </summary>
    public sealed class ProjectionExpressionTests
    {
        private const string Src = """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            using DwarfMapper;
            namespace T16;
            public class S { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class D { public int Id { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper] public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
            public static class Probe
            {
                private static List<S> Data() => new() { new S { Id = 1, Name = "a" }, new S { Id = 3, Name = "c" }, new S { Id = 2, Name = "b" } };
                public static string Composed() => string.Join(",",
                    Data().AsQueryable().Where(s => s.Id > 1).OrderByDescending(s => s.Id).Select(M.ProjectExpression).Select(d => d.Name));
            }
            """;

        [Fact]
        public void The_property_returns_the_tree_the_method_applies_on_every_access()
        {
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(Src);
            Assert.True(asm is not null, "emit failed: " + string.Join(",", errors.Select(e => e.Id)));
            var m = asm!.GetType("T16.M", true)!;
            var s = asm.GetType("T16.S", true)!;
            var exposed = m.GetProperty("ProjectExpression")!.GetValue(null);
            Assert.NotNull(exposed);
            Assert.Same(exposed, m.GetProperty("ProjectExpression")!.GetValue(null));

            // The tree Project() hands a provider, from the SAME assembly: TreeOnlyQueryable always takes the tree path.
            var queryable = Activator.CreateInstance(typeof(TreeOnlyQueryable<>).MakeGenericType(s),
                Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(s)))!;
            var result = (IQueryable)m.GetMethod("Project")!.Invoke(Activator.CreateInstance(m), new[] { queryable })!;
            var applied = ((UnaryExpression)((MethodCallExpression)result.Expression).Arguments[1]).Operand;
            Assert.Same(exposed, applied);
        }

        [Fact]
        public void The_property_composes_with_Where_and_OrderBy()
        {
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(Src);
            Assert.True(asm is not null, "emit failed: " + string.Join(",", errors.Select(e => e.Id)));
            var composed = (string)asm!.GetType("T16.Probe", true)!.GetMethod("Composed")!.Invoke(null, null)!;
            Assert.Equal("c,b", composed);
        }

        [Fact]
        public void The_property_has_the_methods_accessibility()
        {
            const string src = """
                #nullable enable
                using System.Linq;
                using DwarfMapper;
                namespace T16i;
                internal class S { public int Id { get; set; } }
                internal class D { public int Id { get; set; } }
                [DwarfMapper] internal partial class M { internal partial IQueryable<D> Project(IQueryable<S> q); }
                """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("internal static global::System.Linq.Expressions.Expression<global::System.Func<global::T16i.S, global::T16i.D>> ProjectExpression", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_member_already_named_so_reports_DWARF112_and_still_compiles()
        {
            const string src = """
                #nullable enable
                using System.Linq;
                using DwarfMapper;
                namespace T16c;
                public class S { public int Id { get; set; } }
                public class D { public int Id { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial IQueryable<D> Project(IQueryable<S> q);
                    public int ProjectExpression => 42;
                }
                """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.DoesNotContain(" ProjectExpression =>", generated, StringComparison.Ordinal);
            AssertDwarf112(src, "'M' already declares a member of that name");
        }

        [Fact]
        public void A_base_member_named_so_reports_DWARF112()
        {
            const string src = """
                #nullable enable
                using System.Linq;
                using DwarfMapper;
                namespace T16b;
                public class S { public int Id { get; set; } }
                public class D { public int Id { get; set; } }
                public class Base { public static string ProjectExpression = ""; }
                [DwarfMapper] public partial class M : Base { public partial IQueryable<D> Project(IQueryable<S> q); }
                """;
            GeneratorAssert.CompilesClean(src);
            AssertDwarf112(src, "'Base' already declares a member of that name");
        }

        [Fact]
        public void Overloaded_projections_report_DWARF112_and_still_compile()
        {
            const string src = """
                #nullable enable
                using System.Linq;
                using DwarfMapper;
                namespace T16o;
                public class S { public int Id { get; set; } }
                public class S2 { public int Id { get; set; } }
                public class D { public int Id { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial IQueryable<D> Project(IQueryable<S> q);
                    public partial IQueryable<D> Project(IQueryable<S2> q);
                }
                """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.DoesNotContain("ProjectExpression", generated, StringComparison.Ordinal);
            AssertDwarf112(src, "2 projections are named 'Project'");
        }

        private static void AssertDwarf112(string src, string fragment)
        {
            var hits = GeneratorTestHarness.Run(src).Diagnostics
                .Where(d => string.Equals(d.Id, "DWARF112", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(hits);
            Assert.All(hits, d => Assert.Contains(fragment, d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        }
    }
}
