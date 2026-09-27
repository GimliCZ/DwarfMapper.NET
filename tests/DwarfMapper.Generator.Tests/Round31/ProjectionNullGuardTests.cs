// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    public sealed class ProjectionNullGuardTests
    {
        private const string RecordNested = """
            #nullable enable
            using System.Linq;
            using DwarfMapper;
            namespace T02;
            public record Addr(string City);
            public class S { public int Id { get; set; } public Addr? Ship { get; set; } }
            public class AddrDto { public string City { get; set; } = ""; }
            public class D { public int Id { get; set; } public AddrDto? Ship { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
            """;

        [Fact]
        public void Null_guards_in_a_projection_tree_use_reference_equality_not_a_user_operator()
        {
            var tree = ProjectionTree(RecordNested, "T02");
            var offenders = new NullGuardFinder();
            offenders.Visit(tree);
            Assert.True(offenders.UserOperatorGuards.Count == 0,
                "null guard calls a user-defined operator: " + string.Join(", ", offenders.UserOperatorGuards));
        }

        /// <summary>
        ///     RED before T02: the generated projection failed with CS0034, "Operator '==' is ambiguous on operands of
        ///     type 'V' and '&lt;null&gt;'" — generated code that does not compile, with no DwarfMapper diagnostic to
        ///     say why. A type may legally declare several <c>operator ==</c> overloads; the emitter's bare
        ///     <c>x == null</c> then has no unique best candidate.
        /// </summary>
        /// <remarks>
        ///     The task list writes this assertion over the harness's raw compile-error list. It is written through
        ///     <see cref="GeneratorAssert.CompilesClean" /> instead: the assertion is identical ("the emission compiles
        ///     and the generator reported no error"), the repository tracks raw compile-error call sites against a
        ///     baseline that this would have raised for no reason, and the fixture prints the diagnostic ids AND the
        ///     generated source on failure, which a bare <c>Assert.Empty</c> over a collection does not.
        /// </remarks>
        [Fact]
        public void A_nested_type_with_two_equality_operators_still_compiles()
        {
            const string src = """
                #nullable enable
                using System.Linq;
                using DwarfMapper;
                namespace T02b;
                public class V
                {
                    public int X { get; set; }
                    public static bool operator ==(V? a, V? b) => ReferenceEquals(a, b);
                    public static bool operator !=(V? a, V? b) => !(a == b);
                    public static bool operator ==(V? a, string? b) => false;
                    public static bool operator !=(V? a, string? b) => true;
                    public override bool Equals(object? o) => ReferenceEquals(this, o);
                    public override int GetHashCode() => 0;
                }
                public class VDto { public int X { get; set; } }
                public class S { public V? Inner { get; set; } }
                public class D { public VDto? Inner { get; set; } }
                [DwarfMapper] public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
                """;
            GeneratorAssert.CompilesClean(src);
        }

        internal static LambdaExpression ProjectionTree(string source, string ns)
        {
            return ProjectionTrees(source, ns, 1)[0];
        }

        /// <summary>Emits ONE assembly and calls its Project method <paramref name="calls" /> times.</summary>
        internal static List<LambdaExpression> ProjectionTrees(string source, string ns, int calls)
        {
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(source);
            Assert.True(asm is not null, "emit failed: " + string.Join(",", errors.Select(e => e.Id)));
            var s = asm!.GetType(ns + ".S", true)!;
            var m = asm.GetType(ns + ".M", true)!;
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(s))!;
            // Not list.AsQueryable(): since round 31 T13 an EnumerableQuery takes the compiled route and carries no
            // tree. TreeOnlyQueryable is a provider the generated code cannot recognise, so it always gets the tree.
            var queryable = Activator.CreateInstance(typeof(TreeOnlyQueryable<>).MakeGenericType(s), list)!;
            var project = m.GetMethod("Project")!;
            var instance = project.IsStatic ? null : Activator.CreateInstance(m);
            var trees = new List<LambdaExpression>();
            for (var i = 0; i < calls; i++)
            {
                var result = (IQueryable)project.Invoke(instance, [queryable])!;
                var select = (MethodCallExpression)result.Expression;
                trees.Add((LambdaExpression)((UnaryExpression)select.Arguments[1]).Operand);
            }

            return trees;
        }

        private sealed class NullGuardFinder : ExpressionVisitor
        {
            public List<string> UserOperatorGuards { get; } = [];

            protected override Expression VisitBinary(BinaryExpression node)
            {
                var comparesNull = node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual
                                   && (IsNull(node.Left) || IsNull(node.Right));
                if (comparesNull && node.Method is not null)
                {
                    UserOperatorGuards.Add(node.ToString());
                }

                return base.VisitBinary(node);
            }

            private static bool IsNull(Expression e)
            {
                return e is ConstantExpression { Value: null }
                       || e is UnaryExpression { NodeType: ExpressionType.Convert, Operand: ConstantExpression { Value: null } };
            }
        }
    }
}
