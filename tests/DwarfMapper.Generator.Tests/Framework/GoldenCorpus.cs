// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.Generator.Tests.Fuzzing;

namespace DwarfMapper.Generator.Tests.Framework
{
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

            // Round 29 T0.2 fix-round-1 (controller ruling, corpus-hole rule): the widening SpanMap case above
            // never exercises the blit fast path (int -> long differs in size, so it keeps the element loop).
            // A layout-identical struct-element pair is the shape that actually reaches MemoryMarshal.Cast.
            yield return ("SpanMapBlit", """
                                         using DwarfMapper;
                                         using System;
                                         namespace Demo;
                                         public struct Vec3 { public float X; public float Y; public float Z; }
                                         public struct Vec3Dst { public float X; public float Y; public float Z; }
                                         [DwarfMapper] public partial class M { public partial void Map(ReadOnlySpan<Vec3> src, Span<Vec3Dst> dst); }
                                         """, "DwarfGenerator");

            // Round 29 T3.1: the share, in both of its modes, because the two take DIFFERENT decisions in the
            // same method and only a case carrying both pins that they are different. `Proven` is an
            // ImmutableList of a sealed get-only element, which the proof accepts and shares with no attribute;
            // `Asserted` is an IReadOnlyList of the same element, which the proof refuses on principle (an
            // interface is not a guarantee) and which only [MapShare] can share; `Copied` is the same interface
            // WITHOUT the attribute, and it must keep the helper — the manifest is what will notice if the
            // automatic path ever starts accepting an interface.
            yield return ("MapShare", """
                                      using DwarfMapper;
                                      using System.Collections.Generic;
                                      using System.Collections.Immutable;
                                      namespace Demo;
                                      public sealed class Badge { public Badge(string n) { Name = n; } public string Name { get; } }
                                      public class A { public ImmutableList<Badge> Proven { get; set; } = ImmutableList<Badge>.Empty; public IReadOnlyList<Badge> Asserted { get; set; } = System.Array.Empty<Badge>(); public IReadOnlyList<Badge> Copied { get; set; } = System.Array.Empty<Badge>(); }
                                      public class B { public ImmutableList<Badge> Proven { get; set; } = ImmutableList<Badge>.Empty; public IReadOnlyList<Badge> Asserted { get; set; } = System.Array.Empty<Badge>(); public IReadOnlyList<Badge> Copied { get; set; } = System.Array.Empty<Badge>(); }
                                      [DwarfMapper] public partial class M { [MapShare("Asserted")] public partial B Map(A a); }
                                      """, "DwarfGenerator");

            // Round 29 T3.2: the dense fill, with an Offset that is not zero, because the offset is part of the
            // emitted ARITHMETIC and a case at Offset = 0 would pin a subtraction the compiler could fold away.
            // `Counts` is the dense member and `Legacy` is the same dictionary mapped ORDINARILY beside it, so
            // the manifest notices if the directive ever starts reaching a member that did not ask for it — or
            // stops reaching the one that did.
            yield return ("MapDenseEnumKeys", """
                                              using DwarfMapper;
                                              using System.Collections.Generic;
                                              using System.Runtime.CompilerServices;
                                              namespace Demo;
                                              public enum Platform { Web = 1, Ios = 2, Android = 3 }
                                              [InlineArray(3)] public struct Counts3 { private int _e0; }
                                              public class A { public Dictionary<Platform, int> Counts { get; set; } = new(); public Dictionary<Platform, int> Legacy { get; set; } = new(); }
                                              public class B { public Counts3 Counts { get; set; } public Dictionary<Platform, int> Legacy { get; set; } = new(); }
                                              [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts", Offset = 1)] public partial B Map(A a); }
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

            // The HETEROGENEOUS FlattenGraph path: an abstract node base with declared [MapDerivedType] arms,
            // where each concrete type gets its own traversal. Added because scripts/extracted-reach.ps1
            // measured that the corpus reached NONE of the 468 lines that path occupies -- so byte-identity
            // over the manifest was locking nothing there, however green it read. A dedicated snapshot test
            // covers the same shape; this puts it under the manifest as well, where the lock is per-case and
            // survives the snapshot being retired.
            yield return ("HeteroFlattenGraph", """
                                                using DwarfMapper;
                                                using System.Collections.Generic;
                                                namespace Demo;
                                                public abstract class FsNode { public string Name { get; set; } = ""; }
                                                public class Folder : FsNode { public List<FsNode> Children { get; set; } = new(); }
                                                public class FileNode : FsNode { public long Size { get; set; } }
                                                public abstract class FsNodeDto { public string Name { get; set; } = ""; }
                                                public class FolderDto : FsNodeDto { public List<FsNodeDto>? Children { get; set; } }
                                                public class FileNodeDto : FsNodeDto { public long Size { get; set; } }
                                                public class Tree { public FsNode? Root { get; set; } public string Label { get; set; } = ""; }
                                                public class TreeDto { public List<FsNodeDto> Nodes { get; set; } = new(); public string Label { get; set; } = ""; }
                                                [DwarfMapper]
                                                public partial class M
                                                {
                                                    [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                                                    [MapDerivedType<Folder, FolderDto>]
                                                    [MapDerivedType<FileNode, FileNodeDto>]
                                                    public partial TreeDto Map(Tree t);
                                                }
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

            // Round 29 task 2.6. The manifest moved ZERO rows for the payload-edge null-guard fix, and that was
            // the hole rather than the reassurance: no case in it routed a nested REFERENCE member through a map
            // method the USER declared — every nested edge here resolves to a synthesized __DwarfMap_Obj_ helper,
            // which has null-guarded internally since it was written. The three arms of that decision are what
            // these two cases pin. NestedViaDeclaredMap carries all three at once: Inner (nullable -> nullable,
            // lifted), Strict (nullable -> non-nullable, the DWARF070 arm, left forgiven and NOT lifted) and Plain
            // (non-nullable both ways, lifted with the forgiving null arm).
            yield return ("NestedViaDeclaredMap", """
                                                  #nullable enable
                                                  using DwarfMapper;
                                                  namespace Demo;
                                                  public class Child { public int V { get; set; } }
                                                  public class ChildDto { public int V { get; set; } }
                                                  public class A { public Child? Inner { get; set; } public Child? Strict { get; set; } public Child Plain { get; set; } = new(); }
                                                  public class B { public ChildDto? Inner { get; set; } public ChildDto Strict { get; set; } = new(); public ChildDto Plain { get; set; } = new(); }
                                                  [DwarfMapper] public partial class M { public partial B Map(A a); public partial ChildDto ToDto(Child c); }
                                                  """, "DwarfGenerator");

            // [GenerateWrapperMap] itself was unpinned too, in both payload spellings — the nullable one that used
            // to emit CS8604 into the consumer's file and the non-nullable one that used to throw at run time.
            yield return ("WrapperMapPayloadEdge", """
                                                   #nullable enable
                                                   using DwarfMapper;
                                                   namespace Demo;
                                                   public class Child { public int V { get; set; } }
                                                   public class ChildDto { public int V { get; set; } }
                                                   public sealed class Result<T> where T : class
                                                   {
                                                       public Result(T? value, string? error) { Value = value; Error = error; }
                                                       public T? Value { get; }
                                                       public string? Error { get; }
                                                   }
                                                   public sealed class Outcome<T> where T : class
                                                   {
                                                       public Outcome(T value, string? error) { Value = value; Error = error; }
                                                       public T Value { get; }
                                                       public string? Error { get; }
                                                   }
                                                   [DwarfMapper]
                                                   [GenerateWrapperMap(typeof(Result<>))]
                                                   [GenerateWrapperMap(typeof(Outcome<>))]
                                                   [GenerateMap<Child, ChildDto>]
                                                   public partial class M { public partial ChildDto ToDto(Child c); }
                                                   """, "DwarfGenerator");

            // Round 29 task 2.7 — the same blind spot one path over. Phase 5 extra parameters had no golden case
            // at all, and the two defects they carried both need the nullable context to show: the emitted
            // partial dropped the '?' off `Child? lifted` (CS8611 against the user's own declaration) and the
            // phase discarded the null-handling decision, so the body was emitted bare. `#nullable enable` opens
            // the case source because GeneratorRunner defaults to NullableContextOptions.Disable, under which
            // every annotation here is Oblivious and none of these arms is reachable.
            yield return ("NullableExtraParameter", """
                                                    #nullable enable
                                                    using DwarfMapper;
                                                    namespace Demo;
                                                    public class Child { public int V { get; set; } }
                                                    public class ChildDto { public int V { get; set; } }
                                                    public class A { public int Id { get; set; } }
                                                    public class B
                                                    {
                                                        public int Id { get; set; }
                                                        public ChildDto? Lifted { get; set; }
                                                        public ChildDto Forgiven { get; set; } = new();
                                                        public Child Raw { get; set; } = new();
                                                        public int Count { get; set; }
                                                    }
                                                    [DwarfMapper]
                                                    public partial class M
                                                    {
                                                        public partial B Map(A a, Child? lifted, Child? forgiven, Child? raw, int? count);
                                                        public partial ChildDto ToDto(Child c);
                                                    }
                                                    """, "DwarfGenerator");

            // Round 29 task 2.7 fix round 1. The extra parameter's sibling: the SOURCE parameter of the user's
            // own partial. `#nullable enable` for the same reason — GeneratorRunner defaults to Disable, where
            // the annotation is Oblivious and the arm is unreachable.
            yield return ("NullableSourceParameter", """
                                                     #nullable enable
                                                     using DwarfMapper;
                                                     namespace Demo;
                                                     public class A { public int Id { get; set; } }
                                                     public class B { public int Id { get; set; } }
                                                     [DwarfMapper]
                                                     public partial class M
                                                     {
                                                         public partial B Map(A? a);
                                                         public partial void Update(A? a, B b);
                                                     }
                                                     """, "DwarfGenerator");

            // Round 29 task 2.8 — the RETURN half. `#nullable enable` for the same reason again, and here it is
            // load-bearing twice over: under Disable the emitted signature, the extension facade's return type
            // and the registry's null guard are all identical to the unannotated case, so the manifest could
            // never have caught this class of change. The scalar arm's diagnostics were not in the mapper file
            // at all — they were in the two aggregates, which this case's hash does not cover; the marker in
            // GoldenFeatureCoverageTests pins the mapper-file half, and the two standing oracles the rest.
            yield return ("NullableReturnType", """
                                                #nullable enable
                                                using System.Collections.Generic;
                                                using DwarfMapper;
                                                namespace Demo;
                                                public class A { public int Id { get; set; } }
                                                public class B { public int Id { get; set; } }
                                                [DwarfMapper]
                                                public partial class M
                                                {
                                                    public partial B? Map(A a);
                                                    public partial List<B?> Many(List<A> a);
                                                    public partial void Update(A a, B? b);
                                                }
                                                """, "DwarfGenerator");

            // Round 29 task 2.8, defect B — the async-stream element edge. `#nullable enable` again: under
            // Disable the element annotation is Oblivious, the null decision is None, and the emitted loop is
            // byte-identical to the hand-written one this replaced, so the manifest could not see the change.
            // Both destination shapes, because they take different arms of the shared element expression.
            yield return ("AsyncStreamNullableElement", """
                                                       #nullable enable
                                                       using System.Collections.Generic;
                                                       using DwarfMapper;
                                                       namespace Demo;
                                                       public class A { public int Id { get; set; } }
                                                       public class B { public int Id { get; set; } }
                                                       public class Child { public int V { get; set; } }
                                                       public class ChildDto { public int V { get; set; } }
                                                       [DwarfMapper]
                                                       public partial class M
                                                       {
                                                           public partial IAsyncEnumerable<B> Strict(IAsyncEnumerable<A?> a);
                                                           public partial IAsyncEnumerable<ChildDto?> Lifted(IAsyncEnumerable<Child?> c);
                                                           public partial ChildDto ToDto(Child c);
                                                       }
                                                       """, "DwarfGenerator");

            // Round 29 task 2.9 — the element edge reached through a map method the USER declared. The three
            // cases above pin the MEMBER path's three arms and the async stream's two; none of them routes a
            // nullable ELEMENT through a declared converter, which is exactly the arm CollectionConverter's own
            // IsSynthesized proxy answered for and got wrong. `#nullable enable` again, and for the same reason:
            // under Disable the element annotation is Oblivious, the forgiveness is unreachable and this case
            // would be byte-identical to a pre-fix run. Strict is the forgiven arm, Lifted the arm that must NOT
            // move, and Tolerant the null-accepting converter that must keep its null.
            yield return ("ElementViaDeclaredMap", """
                                                   #nullable enable
                                                   using System.Collections.Generic;
                                                   using DwarfMapper;
                                                   namespace Demo;
                                                   public class Child { public int V { get; set; } }
                                                   public class ChildDto { public int V { get; set; } }
                                                   public class Loose { public int V { get; set; } }
                                                   public class LooseDto { public int V { get; set; } }
                                                   public class A
                                                   {
                                                       public List<Child?> Strict { get; set; } = new();
                                                       public Dictionary<string, Child?> Lookup { get; set; } = new();
                                                       public List<Child?> Lifted { get; set; } = new();
                                                       public List<Loose?> Tolerant { get; set; } = new();
                                                   }
                                                   public class B
                                                   {
                                                       public List<ChildDto> Strict { get; set; } = new();
                                                       public Dictionary<string, ChildDto> Lookup { get; set; } = new();
                                                       public List<ChildDto?> Lifted { get; set; } = new();
                                                       public List<LooseDto> Tolerant { get; set; } = new();
                                                   }
                                                   [DwarfMapper]
                                                   public partial class M
                                                   {
                                                       public partial B Map(A a);
                                                       public partial ChildDto ToDto(Child c);
                                                       public LooseDto ToLoose(Loose? l) => new LooseDto { V = l?.V ?? 0 };
                                                   }
                                                   """, "DwarfGenerator");

            // Round 29 task 2.9, the RETURN half. Nothing in the corpus had ever declared a converter that
            // returns a nullable reference — which is why the manifest moved zero EMISSION rows for the fix, and
            // why that silence is the hole rather than the reassurance. Strict is the forgiven member arm, Free
            // the nullable destination that must gain nothing, and the collection and dictionary members prove
            // the two element builders answer it the same way the member emitter does.
            yield return ("NullableReturnConverter", """
                                                     #nullable enable
                                                     using System.Collections.Generic;
                                                     using DwarfMapper;
                                                     namespace Demo;
                                                     public class Child { public int V { get; set; } }
                                                     public class ChildDto { public int V { get; set; } }
                                                     public class A
                                                     {
                                                         public Child Strict { get; set; } = new();
                                                         public Child Free { get; set; } = new();
                                                         public List<Child> Items { get; set; } = new();
                                                         public Dictionary<string, Child> Lookup { get; set; } = new();
                                                         public Dictionary<Child, int> Counts { get; set; } = new();
                                                     }
                                                     public class B
                                                     {
                                                         public ChildDto Strict { get; set; } = new();
                                                         public ChildDto? Free { get; set; }
                                                         public List<ChildDto> Items { get; set; } = new();
                                                         public Dictionary<string, ChildDto> Lookup { get; set; } = new();
                                                         public Dictionary<ChildDto, int> Counts { get; set; } = new();
                                                     }
                                                     [DwarfMapper]
                                                     public partial class M
                                                     {
                                                         public partial B Map(A a);
                                                         public partial ChildDto? ToDto(Child c);
                                                     }
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

            // Non-default EnumStrategy: EnumStrategy.ByName is the documented default, so EnumByName above would
            // emit byte-identical output even if attribute reading were completely broken. Source/destination
            // members are deliberately named differently (A/B vs X/Y, same underlying values) so that if the
            // strategy were NOT actually wired to ByValue and fell back to ByName, the by-name completeness check
            // would report DWARF015 as a build error — a broken wiring is caught even before the marker check runs.
            yield return ("EnumByValue", """
                                         using DwarfMapper;
                                         namespace Demo;
                                         public enum SrcCode { A = 1, B = 2 }
                                         public enum DstCode { X = 1, Y = 2 }
                                         public class Src { public SrcCode C { get; set; } }
                                         public class Dst { public DstCode C { get; set; } }
                                         [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)] public partial class M { public partial Dst Map(Src a); }
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

            // Non-default NullStrategy: NullStrategy.Throw is the documented default, so NullStrategyThrow above
            // would emit byte-identical output even if attribute reading were completely broken. This case pins
            // the SetDefault codegen shape (.GetValueOrDefault()), which only fires when the strategy is actually
            // read off the attribute.
            yield return ("NullStrategySetDefault", """
                                                    using DwarfMapper;
                                                    namespace Demo;
                                                    public class A { public int? V { get; set; } }
                                                    public class B { public int V { get; set; } }
                                                    [DwarfMapper(NullStrategy = NullStrategy.SetDefault)] public partial class M { public partial B Map(A a); }
                                                    """, "DwarfGenerator");

            // Round 30 (corpus hole, owner-approved): no case set SkipNullSourceMembers, so the golden corpus never
            // executed ApplySkipNullSourceMembers past its option check — the deferrable-target walk and the
            // IsNullCapableSourceMember lookup were locked by nothing, which extracted-reach measured (49/52) the
            // moment that predicate sat in a seam file. One member per arm of the decision: Name (nullable
            // REFERENCE into a non-nullable target — guarded, and its DWARF070 cleared), Count (nullable value type —
            // guarded), Id (non-nullable value type — never null, left in the initializer) and Code (init-only
            // target — cannot be deferred, left in the initializer). `#nullable enable` because under the runner's
            // default Disable the reference annotation is Oblivious and the reference arm is not the one taken.
            yield return ("SkipNullSourceMembers", """
                                                   #nullable enable
                                                   using DwarfMapper;
                                                   namespace Demo;
                                                   public class A { public int Id { get; set; } public string? Name { get; set; } public int? Count { get; set; } public string? Code { get; set; } }
                                                   public class B { public int Id { get; set; } public string Name { get; set; } = ""; public int Count { get; set; } public string? Code { get; init; } }
                                                   [DwarfMapper(SkipNullSourceMembers = true)] public partial class M { public partial B Map(A a); }
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

            yield return ("RegistryInheritedDestination", """
                                                          using DwarfMapper;
                                                          namespace Demo;
                                                          public class DtoBase { public int Id { get; set; } }
                                                          public class Dto : DtoBase { public string Name { get; set; } = ""; }
                                                          [MapTo(typeof(Dto))]
                                                          public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }
                                                          """, "MapToGenerator");

            yield return ("RegistryInheritedSource", """
                                                     using DwarfMapper;
                                                     namespace Demo;
                                                     public class SrcBase { public int Id { get; set; } }
                                                     [MapTo(typeof(Dto))]
                                                     public class Src : SrcBase { public string Name { get; set; } = ""; }
                                                     public class Dto { public int Id { get; set; } public string Name { get; set; } = ""; }
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
}
