// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     Research P5 Case 2: an <c>IQueryable</c> built from a list is LINQ-to-objects in disguise, and handing it the
    ///     expression tree makes LINQ compile the tree on every enumeration (measured 985 µs → 1.3 µs for 10 rows). The
    ///     generated projection routes an <c>EnumerableQuery</c> to a delegate compiled from the SAME lambda text.
    ///     Parity is the whole contract: the routed result must equal what the tree gives, row for row, so the oracle
    ///     is <see cref="TreeOnlyQueryable{T}" />, which always takes the tree.
    /// </summary>
    public sealed class InMemoryProjectionRoutingTests
    {
        private const string Flat = """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            using DwarfMapper;
            namespace T13;
            public class S { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class D { public int Id { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper] public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
            public static class Seed
            {
                public static List<S> Data() => new() { new S { Id = 1, Name = "a" }, new S { Id = 2, Name = "b" } };
            }
            """;

        [Fact]
        public void A_list_backed_queryable_takes_the_compiled_path()
        {
            var (routed, _) = Run(Flat, "T13");
            Assert.IsAssignableFrom<ConstantExpression>(routed.Expression);
        }

        [Fact]
        public void A_provider_queryable_still_gets_the_tree()
        {
            var (_, tree) = Run(Flat, "T13");
            Assert.IsAssignableFrom<MethodCallExpression>(tree.Expression);
        }

        public static TheoryData<string, string> Shapes()
        {
            return new TheoryData<string, string>
            {
                { "T13", Flat },
                {
                    "T13n", """
                        #nullable enable
                        using System.Collections.Generic;
                        using System.Linq;
                        using DwarfMapper;
                        namespace T13n;
                        public record Addr(string City);
                        public class AddrDto { public string City { get; set; } = ""; }
                        public class S { public int Id { get; set; } public Addr? Ship { get; set; } }
                        public class D { public int Id { get; set; } public AddrDto? Ship { get; set; } }
                        [DwarfMapper] public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
                        public static class Seed
                        {
                            public static List<S> Data() => new() { new S { Id = 1, Ship = new Addr("Brno") }, new S { Id = 2, Ship = null } };
                        }
                        """
                },
                {
                    "T13c", """
                        #nullable enable
                        using System.Collections.Generic;
                        using System.Linq;
                        using DwarfMapper;
                        namespace T13c;
                        public class Line { public int Qty { get; set; } }
                        public class LineDto { public int Qty { get; set; } }
                        public class S { public int Id { get; set; } public List<Line> Lines { get; set; } = new(); }
                        public class D { public int Id { get; set; } public List<LineDto> Lines { get; set; } = new(); }
                        [DwarfMapper] public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
                        public static class Seed
                        {
                            public static List<S> Data() => new()
                            {
                                new S { Id = 1, Lines = new() { new Line { Qty = 3 }, new Line { Qty = 4 } } },
                                new S { Id = 2, Lines = new() },
                            };
                        }
                        """
                },
                {
                    "T13k", """
                        #nullable enable
                        using System.Collections.Generic;
                        using System.Linq;
                        using DwarfMapper;
                        namespace T13k;
                        public class S { public int X { get; set; } public string Extra { get; set; } = ""; }
                        public class D
                        {
                            public D(int x) { X = x; }
                            public int X { get; }
                            public string Extra { get; init; } = "";
                        }
                        [DwarfMapper] public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
                        public static class Seed
                        {
                            public static List<S> Data() => new() { new S { X = 7, Extra = "e" }, new S { X = 8 } };
                        }
                        """
                },
                {
                    "T13e", """
                        #nullable enable
                        using System.Collections.Generic;
                        using System.Linq;
                        using DwarfMapper;
                        namespace T13e;
                        public enum Color { Red, Green }
                        public class S { public Color C { get; set; } }
                        public class D { public Color C { get; set; } } // same enum: a projection refuses enum->enum conversion
                        [DwarfMapper] public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
                        public static class Seed
                        {
                            public static List<S> Data() => new() { new S { C = Color.Green }, new S { C = Color.Red } };
                        }
                        """
                },
            };
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void Tree_and_compiled_paths_return_equal_results(string ns, string source)
        {
            var (routed, tree) = Run(source, ns);
            var routedText = Dump(routed);
            Assert.Equal(Dump(tree), routedText);
            Assert.NotEqual("[]", routedText); // an empty result would make the parity vacuous
        }

        private static (IQueryable Routed, IQueryable Tree) Run(string source, string ns)
        {
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(source);
            Assert.True(asm is not null, "emit failed: " + string.Join(",", errors.Select(e => e.Id)));
            var s = asm!.GetType(ns + ".S", throwOnError: true)!;
            var m = asm.GetType(ns + ".M", throwOnError: true)!;
            var data = (IEnumerable)asm.GetType(ns + ".Seed", throwOnError: true)!.GetMethod("Data")!.Invoke(null, null)!;
            var project = m.GetMethod("Project")!;
            var instance = project.IsStatic ? null : Activator.CreateInstance(m);

            var asQueryable = typeof(Queryable).GetMethods()
                .First(x => x.Name == nameof(Queryable.AsQueryable) && x.IsGenericMethod)
                .MakeGenericMethod(s);
            var listQuery = asQueryable.Invoke(null, new object[] { data })!;
            var treeQuery = Activator.CreateInstance(typeof(TreeOnlyQueryable<>).MakeGenericType(s), data)!;

            return ((IQueryable)project.Invoke(instance, new[] { listQuery })!,
                (IQueryable)project.Invoke(instance, new[] { treeQuery })!);
        }

        private static string Dump(object? value)
        {
            var sb = new StringBuilder();
            Append(sb, value);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, object? value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return;
                case string str:
                    sb.Append('"').Append(str).Append('"');
                    return;
                case IFormattable f when value.GetType().IsPrimitive || value.GetType().IsEnum:
                    sb.Append(f.ToString(null, CultureInfo.InvariantCulture));
                    return;
                case IEnumerable items:
                    sb.Append('[');
                    foreach (var item in items)
                    {
                        Append(sb, item);
                        sb.Append(';');
                    }

                    sb.Append(']');
                    return;
            }

            sb.Append('{');
            foreach (var p in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                sb.Append(p.Name).Append('=');
                Append(sb, p.GetValue(value));
                sb.Append(',');
            }

            sb.Append('}');
        }
    }
}
