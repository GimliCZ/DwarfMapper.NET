// SPDX-License-Identifier: GPL-2.0-only
using System;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     Research P5a: every <c>Project()</c> call used to build its expression tree afresh — measured 15.0 µs / 5,400 B
    ///     per call against 1.0 µs / 424 B for a tree built once. A projection takes exactly one parameter, so there is
    ///     never a captured argument that would make a shared tree wrong; the tree now lives in a static readonly field.
    /// </summary>
    public sealed class ProjectionHoistingTests
    {
        private const string Src = """
            #nullable enable
            using System.Linq;
            using DwarfMapper;
            namespace T10;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
            """;

        [Fact]
        public void The_projection_tree_is_built_once()
        {
            var trees = ProjectionNullGuardTests.ProjectionTrees(Src, "T10", calls: 2); // one assembly, two calls
            Assert.Same(trees[0], trees[1]);
        }

        [Fact]
        public void The_tree_lives_in_a_static_readonly_field()
        {
            var generated = GeneratorTestHarness.Run(Src).GeneratedSource;
            Assert.Contains("private static readonly global::System.Linq.Expressions.Expression<global::System.Func<", generated, StringComparison.Ordinal);
            Assert.Contains("__dwarf_proj_", generated, StringComparison.Ordinal);
        }

        [Fact] // the leading empty-TargetName entry: `new D(__s.X) { Extra = … }` must hoist whole
        public void A_constructor_projection_with_extra_members_is_hoisted_and_still_maps()
        {
            const string src = """
                #nullable enable
                using System.Linq;
                using DwarfMapper;
                namespace T10c;
                public class S { public int X { get; set; } public string Extra { get; set; } = ""; }
                public class D
                {
                    public D(int x) { X = x; }
                    public int X { get; }
                    public string Extra { get; init; } = "";
                }
                [DwarfMapper] public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
                """;
            var trees = ProjectionNullGuardTests.ProjectionTrees(src, "T10c", calls: 2);
            Assert.Same(trees[0], trees[1]);
            var body = trees[0].Body.ToString();
            Assert.Contains("new D(", body, StringComparison.Ordinal);
            Assert.Contains("Extra =", body, StringComparison.Ordinal);
        }

        [Fact] // the ordinal keeps overloads apart; `@class` must not leak its '@' into the middle of an identifier
        public void Overloads_and_escaped_names_get_distinct_legal_fields()
        {
            const string src = """
                #nullable enable
                using System.Linq;
                using DwarfMapper;
                namespace T10o;
                public class S { public int Id { get; set; } }
                public class S2 { public int Id { get; set; } }
                public class D { public int Id { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial IQueryable<D> @class(IQueryable<S> q);
                    public partial IQueryable<D> @class(IQueryable<S2> q);
                }
                """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Equal(2, generated.Split("private static readonly global::System.Linq.Expressions").Length - 1);
            Assert.DoesNotContain("__dwarf_proj_@", generated, StringComparison.Ordinal);
        }
    }
}
