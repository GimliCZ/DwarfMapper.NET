// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.Generator.Tests.Fuzzing;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>
///     The pinned corpus. Ids are stable so schema growth appears as explicit added/removed manifest lines
///     rather than an unreadable reshuffle. Three axes, because the type axis alone would miss the entire
///     feature surface (FlattenGraph, projection, span, async-stream, derived types, hooks, ambient registry)
///     and the second generator.
/// </summary>
internal static class GoldenCorpus
{
    /// <summary>Fixed seed range for the fuzz axis. Fixed, not random, or the manifest could never be stable.</summary>
    public const int SyntheticSeedCount = 40;

    public static IReadOnlyList<GoldenCase> Cases()
    {
        var cases = new List<GoldenCase>();

        // ── Type axis: the combinatorial matrices ────────────────────────────
        foreach (var cell in CombinatorialSchema.DepthOneMatrix().Concat(CombinatorialSchema.DepthTwoMatrix()))
            cases.Add(new GoldenCase(
                $"cmb:{cell.BasicType}|{cell.ShapeName}|{cell.Variant}",
                cell.Source,
                "DwarfGenerator"));

        // ── Fuzz axis: a FIXED seed range ────────────────────────────────────
        for (var seed = 0; seed < SyntheticSeedCount; seed++)
            cases.Add(new GoldenCase(
                "syn:seed-" + seed.ToString("D4", CultureInfo.InvariantCulture),
                SyntheticSchema.Generate(seed),
                "DwarfGenerator"));

        // ── Feature axis: one case per feature, incl. the registry generator ─
        foreach (var (id, source, generatorName) in FeatureCases())
            cases.Add(new GoldenCase("feat:" + id, source, generatorName));

        // Deterministic order: the manifest is compared line by line.
        return cases.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<(string Id, string Source, string GeneratorName)> FeatureCases()
    {
        yield return ("Basic", Mapper("public partial B Map(A a);"), "DwarfGenerator");

        yield return ("UpdateInto", Mapper("public partial void Update(A a, B b);"), "DwarfGenerator");

        yield return ("Projection", """
                                    using DwarfMapper;
                                    using System.Linq;
                                    namespace Demo;
                                    public class A { public int X { get; set; } }
                                    public class B { public int X { get; set; } }
                                    [DwarfMapper] public partial class M { public partial IQueryable<B> Project(IQueryable<A> q); }
                                    """, "DwarfGenerator");

        yield return ("SpanMap", """
                                 using DwarfMapper;
                                 using System;
                                 namespace Demo;
                                 [DwarfMapper] public partial class M { public partial void Map(ReadOnlySpan<int> src, Span<long> dst); }
                                 """, "DwarfGenerator");

        yield return ("AsyncStream", """
                                     using DwarfMapper;
                                     using System.Collections.Generic;
                                     namespace Demo;
                                     public class A { public int X { get; set; } }
                                     public class B { public int X { get; set; } }
                                     [DwarfMapper] public partial class M { public partial IAsyncEnumerable<B> Map(IAsyncEnumerable<A> src); }
                                     """, "DwarfGenerator");

        yield return ("FlattenGraph", """
                                      using DwarfMapper;
                                      using System.Collections.Generic;
                                      namespace Demo;
                                      public class Node { public int Id { get; set; } public List<string> Tags { get; set; } = new(); public Node? Next { get; set; } }
                                      public class NodeDto { public int Id { get; set; } public List<string> Tags { get; set; } = new(); }
                                      public class Root { public Node? Entry { get; set; } }
                                      public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                                      [DwarfMapper] public partial class M { [FlattenGraph("Entry", "Nodes")] public partial RootDto Map(Root r); }
                                      """, "DwarfGenerator");

        yield return ("Flatten", """
                                 using DwarfMapper;
                                 namespace Demo;
                                 public class Addr { public string City { get; set; } = ""; }
                                 public class A { public Addr Address { get; set; } = new(); }
                                 public class B { public string City { get; set; } = ""; }
                                 [DwarfMapper] public partial class M { [Flatten(nameof(A.Address))] public partial B Map(A a); }
                                 """, "DwarfGenerator");

        yield return ("ConstructorMapping", """
                                            using DwarfMapper;
                                            namespace Demo;
                                            public class A { public int X { get; set; } }
                                            public class B { public B(int x) { X = x; } public int X { get; } }
                                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                                            """, "DwarfGenerator");

        yield return ("EnumByName", """
                                    using DwarfMapper;
                                    namespace Demo;
                                    public enum SrcColor { Red = 1, Green = 2 }
                                    public enum DstColor { Red = 1, Green = 2 }
                                    public class A { public SrcColor C { get; set; } }
                                    public class B { public DstColor C { get; set; } }
                                    [DwarfMapper(EnumStrategy = EnumStrategy.ByName)] public partial class M { public partial B Map(A a); }
                                    """, "DwarfGenerator");

        yield return ("FlagsEnumFromString", """
                                             using DwarfMapper;
                                             using System;
                                             namespace Demo;
                                             [Flags] public enum Perm { None = 0, Read = 1, Write = 2 }
                                             public class A { public string P { get; set; } = ""; }
                                             public class B { public Perm P { get; set; } }
                                             [DwarfMapper(EnumStrategy = EnumStrategy.ByName)] public partial class M { public partial B Map(A a); }
                                             """, "DwarfGenerator");

        yield return ("NullStrategyThrow", """
                                           using DwarfMapper;
                                           namespace Demo;
                                           public class A { public int? V { get; set; } }
                                           public class B { public int V { get; set; } }
                                           [DwarfMapper(NullStrategy = NullStrategy.Throw)] public partial class M { public partial B Map(A a); }
                                           """, "DwarfGenerator");

        yield return ("PreserveReferences", """
                                            using DwarfMapper;
                                            namespace Demo;
                                            public class Node { public int V { get; set; } public Node? Next { get; set; } }
                                            public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                                            [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)] public partial class M { public partial NodeDto Map(Node n); }
                                            """, "DwarfGenerator");

        yield return ("DerivedTypes", """
                                      using DwarfMapper;
                                      namespace Demo;
                                      public class A { public int X { get; set; } }
                                      public class ADerived : A { public int Y { get; set; } }
                                      public class B { public int X { get; set; } }
                                      public class BDerived : B { public int Y { get; set; } }
                                      [DwarfMapper] public partial class M { [MapDerivedType(typeof(ADerived), typeof(BDerived))] public partial B Map(A a); }
                                      """, "DwarfGenerator");

        yield return ("Hooks", """
                               using DwarfMapper;
                               namespace Demo;
                               public class A { public int X { get; set; } }
                               public class B { public int X { get; set; } }
                               [DwarfMapper] public partial class M
                               {
                                   public partial B Map(A a);
                                   [AfterMap] private static void After(A a, B b) { }
                               }
                               """, "DwarfGenerator");

        yield return ("ReverseMap", """
                                    using DwarfMapper;
                                    namespace Demo;
                                    public class A { public int X { get; set; } }
                                    public class B { public int X { get; set; } }
                                    [DwarfMapper] public partial class M
                                    {
                                        [RoundTrip] public partial B ToB(A a);
                                        public partial A FromB(B b);
                                    }
                                    """, "DwarfGenerator");

        yield return ("CoLocatedGenerateMap", """
                                              using DwarfMapper;
                                              namespace Demo;
                                              public class A { public int X { get; set; } }
                                              [GenerateMap<A, B>] public sealed class B { public int X { get; set; } }
                                              """, "DwarfGenerator");

        yield return ("RegistryBasic", """
                                       using DwarfMapper;
                                       namespace Demo;
                                       [MapTo(typeof(Dto))] public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }
                                       public class Dto { public int Id { get; set; } public string Name { get; set; } = ""; }
                                       """, "MapToGenerator");

        yield return ("RegistryCollection", """
                                            using DwarfMapper;
                                            using System.Collections.Generic;
                                            namespace Demo;
                                            [MapTo(typeof(Dto))] public class Src { public List<int> Xs { get; set; } = new(); }
                                            public class Dto { public List<long> Xs { get; set; } = new(); }
                                            """, "MapToGenerator");

        yield return ("RegistryNested", """
                                        using DwarfMapper;
                                        namespace Demo;
                                        public class Inner { public int V { get; set; } }
                                        public class InnerDto { public int V { get; set; } }
                                        [MapTo(typeof(Dto))] public class Src { public Inner I { get; set; } = new(); }
                                        public class Dto { public InnerDto I { get; set; } = new(); }
                                        """, "MapToGenerator");
    }

    private static string Mapper(string methodDeclaration)
    {
        return $$"""
                 using DwarfMapper;
                 namespace Demo;
                 public class A { public int X { get; set; } public string Name { get; set; } = ""; }
                 public class B { public int X { get; set; } public string Name { get; set; } = ""; }
                 [DwarfMapper] public partial class M { {{methodDeclaration}} }
                 """;
    }
}
