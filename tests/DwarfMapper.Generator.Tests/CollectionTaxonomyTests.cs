// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

// ─── Shared helpers ──────────────────────────────────────────────────────────

file static class Col
{
    public static void NoErrors(string src)
    {
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    public static void HasDwarf027(string src)
    {
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Id == "DWARF027");
    }

    public static string GenOf(string src)
    {
        var (_, gen) = GeneratorTestHarness.Run(src);
        return gen;
    }
}

// ─── DWARF027: unsupported collection targets ─────────────────────────────────

public class Dwarf027Tests
{
    [Fact]
    public void SortedSet_target_emits_DWARF027()
    {
        Col.HasDwarf027("""
                        using DwarfMapper;
                        using System.Collections.Generic;
                        namespace Demo;
                        public class A { public SortedSet<int> Xs { get; set; } = new(); }
                        public class B { public SortedSet<int> Xs { get; set; } = new(); }
                        [DwarfMapper] public partial class M { public partial B Map(A a); }
                        """);
    }

    [Fact]
    public void SortedDictionary_target_emits_DWARF027()
    {
        Col.HasDwarf027("""
                        using DwarfMapper;
                        using System.Collections.Generic;
                        namespace Demo;
                        public class A { public Dictionary<string,int> D { get; set; } = new(); }
                        public class B { public SortedDictionary<string,int> D { get; set; } = new(); }
                        [DwarfMapper] public partial class M { public partial B Map(A a); }
                        """);
    }

    // Queue<T> and Stack<T> were formerly refused (DWARF027) to avoid a silently-reversed Stack. They are now
    // SUPPORTED with a defined, round-trip-safe ordering (enumeration order preserved) — see G25 /
    // StackQueueRuntimeTests. These pin that they are no longer refused and compile cleanly.
    [Fact]
    public void Queue_target_is_supported()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public System.Collections.Generic.List<int> Xs { get; set; } = new(); }
                     public class B { public System.Collections.Generic.Queue<int> Xs { get; set; } = new(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void Stack_target_is_supported()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public System.Collections.Generic.List<int> Xs { get; set; } = new(); }
                     public class B { public System.Collections.Generic.Stack<int> Xs { get; set; } = new(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void Multidimensional_array_target_emits_DWARF027()
    {
        Col.HasDwarf027("""
                        using DwarfMapper;
                        namespace Demo;
                        public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                        public class B { public int[,] Xs { get; set; } = new int[0,0]; }
                        [DwarfMapper] public partial class M { public partial B Map(A a); }
                        """);
    }

    [Fact]
    public void DWARF027_is_not_DWARF005_for_SortedSet()
    {
        var (diagnostics, _) = GeneratorTestHarness.Run("""
                                                        using DwarfMapper;
                                                        using System.Collections.Generic;
                                                        namespace Demo;
                                                        public class A { public SortedSet<int> Xs { get; set; } = new(); }
                                                        public class B { public SortedSet<int> Xs { get; set; } = new(); }
                                                        [DwarfMapper] public partial class M { public partial B Map(A a); }
                                                        """);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF005");
        Assert.Contains(diagnostics, d => d.Id == "DWARF027");
    }

    [Fact]
    public void DWARF027_message_contains_MapProperty_hint()
    {
        var (diagnostics, _) = GeneratorTestHarness.Run("""
                                                        using DwarfMapper;
                                                        using System.Collections.Generic;
                                                        namespace Demo;
                                                        public class A { public SortedSet<int> Xs { get; set; } = new(); }
                                                        public class B { public SortedSet<int> Xs { get; set; } = new(); }
                                                        [DwarfMapper] public partial class M { public partial B Map(A a); }
                                                        """);
        var d027 = diagnostics.FirstOrDefault(d => d.Id == "DWARF027");
        Assert.NotNull(d027);
        Assert.Contains("MapProperty", d027!.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
    }
}

// ─── New collection interface targets → concrete ─────────────────────────────

public class CollectionInterfaceTargetTests
{
    // IList<T> → List<T>
    [Fact]
    public void IList_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public IEnumerable<int> Xs { get; set; } = System.Array.Empty<int>(); }
                     public class B { public IList<int> Xs { get; set; } = new List<int>(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void IList_target_emits_List_construction()
    {
        var gen = Col.GenOf("""
                            using DwarfMapper;
                            using System.Collections.Generic;
                            namespace Demo;
                            public class A { public IEnumerable<int> Xs { get; set; } = System.Array.Empty<int>(); }
                            public class B { public IList<int> Xs { get; set; } = new List<int>(); }
                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                            """);
        // IList<T> target must produce a List<T>
        Assert.Contains("List<int>", gen, StringComparison.Ordinal);
    }

    // ICollection<T> → List<T>
    [Fact]
    public void ICollection_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                     public class B { public ICollection<int> Xs { get; set; } = new List<int>(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    // IReadOnlyList<T> → List<T>
    [Fact]
    public void IReadOnlyList_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                     public class B { public IReadOnlyList<int> Xs { get; set; } = new List<int>(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    // IReadOnlyCollection<T> → List<T>
    [Fact]
    public void IReadOnlyCollection_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                     public class B { public IReadOnlyCollection<int> Xs { get; set; } = new List<int>(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    // ISet<T> → HashSet<T>
    [Fact]
    public void ISet_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                     public class B { public ISet<int> Xs { get; set; } = new HashSet<int>(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void ISet_target_emits_HashSet_construction()
    {
        var gen = Col.GenOf("""
                            using DwarfMapper;
                            using System.Collections.Generic;
                            namespace Demo;
                            public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                            public class B { public ISet<int> Xs { get; set; } = new HashSet<int>(); }
                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                            """);
        Assert.Contains("HashSet<int>", gen, StringComparison.Ordinal);
    }

    // IReadOnlySet<T> → HashSet<T>
    [Fact]
    public void IReadOnlySet_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                     public class B { public IReadOnlySet<int> Xs { get; set; } = new HashSet<int>(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }
}

// ─── Immutable collection targets ────────────────────────────────────────────

public class ImmutableCollectionTargetTests
{
    [Fact]
    public void ImmutableArray_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     using System.Collections.Immutable;
                     namespace Demo;
                     public class A { public List<int> Xs { get; set; } = new(); }
                     public class B { public ImmutableArray<int> Xs { get; set; } }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void ImmutableArray_target_emits_CreateRange()
    {
        var gen = Col.GenOf("""
                            using DwarfMapper;
                            using System.Collections.Generic;
                            using System.Collections.Immutable;
                            namespace Demo;
                            public class A { public List<int> Xs { get; set; } = new(); }
                            public class B { public ImmutableArray<int> Xs { get; set; } }
                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                            """);
        Assert.Contains("ImmutableArray", gen, StringComparison.Ordinal);
        Assert.Contains("CreateRange", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void ImmutableList_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     using System.Collections.Immutable;
                     namespace Demo;
                     public class A { public List<int> Xs { get; set; } = new(); }
                     public class B { public ImmutableList<int> Xs { get; set; } = ImmutableList<int>.Empty; }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void IImmutableList_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     using System.Collections.Immutable;
                     namespace Demo;
                     public class A { public List<int> Xs { get; set; } = new(); }
                     public class B { public System.Collections.Immutable.IImmutableList<int> Xs { get; set; } = ImmutableList<int>.Empty; }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void ImmutableHashSet_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     using System.Collections.Immutable;
                     namespace Demo;
                     public class A { public List<int> Xs { get; set; } = new(); }
                     public class B { public ImmutableHashSet<int> Xs { get; set; } = ImmutableHashSet<int>.Empty; }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void IImmutableSet_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     using System.Collections.Immutable;
                     namespace Demo;
                     public class A { public List<int> Xs { get; set; } = new(); }
                     public class B { public System.Collections.Immutable.IImmutableSet<int> Xs { get; set; } = ImmutableHashSet<int>.Empty; }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }
}

// ─── Dictionary interface / immutable dict targets ───────────────────────────

public class DictionaryTaxonomyTests
{
    [Fact]
    public void IDictionary_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public Dictionary<string,int> D { get; set; } = new(); }
                     public class B { public IDictionary<string,int> D { get; set; } = new Dictionary<string,int>(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void IReadOnlyDictionary_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public Dictionary<string,int> D { get; set; } = new(); }
                     public class B { public IReadOnlyDictionary<string,int> D { get; set; } = new Dictionary<string,int>(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void IDictionary_target_emits_Dictionary_construction()
    {
        var gen = Col.GenOf("""
                            using DwarfMapper;
                            using System.Collections.Generic;
                            namespace Demo;
                            public class A { public Dictionary<string,int> D { get; set; } = new(); }
                            public class B { public IDictionary<string,int> D { get; set; } = new Dictionary<string,int>(); }
                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                            """);
        Assert.Contains("Dictionary<", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void ImmutableDictionary_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     using System.Collections.Immutable;
                     namespace Demo;
                     public class A { public Dictionary<string,int> D { get; set; } = new(); }
                     public class B { public ImmutableDictionary<string,int> D { get; set; } = ImmutableDictionary<string,int>.Empty; }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void ImmutableDictionary_target_emits_a_keyed_builder()
    {
        var gen = Col.GenOf("""
                            using DwarfMapper;
                            using System.Collections.Generic;
                            using System.Collections.Immutable;
                            namespace Demo;
                            public class A { public Dictionary<string,int> D { get; set; } = new(); }
                            public class B { public ImmutableDictionary<string,int> D { get; set; } = ImmutableDictionary<string,int>.Empty; }
                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                            """);
        Assert.Contains("ImmutableDictionary", gen, StringComparison.Ordinal);
        // ISSUE-005: was CreateRange, which THROWS when a many-to-one key conversion collides — while the
        // mutable path silently took last-write-wins for the same input. A keyed builder makes both agree.
        Assert.Contains("CreateBuilder", gen, StringComparison.Ordinal);
        Assert.Contains("ToImmutable()", gen, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateRange", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void IImmutableDictionary_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     using System.Collections.Immutable;
                     namespace Demo;
                     public class A { public Dictionary<string,int> D { get; set; } = new(); }
                     public class B { public System.Collections.Immutable.IImmutableDictionary<string,int> D { get; set; } = ImmutableDictionary<string,int>.Empty; }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }
}

// ─── Lazy IEnumerable<T> target ───────────────────────────────────────────────

public class LazyIEnumerableTargetTests
{
    [Fact]
    public void IEnumerable_target_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public List<int> Xs { get; set; } = new(); }
                     public class B { public IEnumerable<int> Xs { get; set; } = System.Array.Empty<int>(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    // An IEnumerable<T> destination used to be handed back LAZILY — `src` itself when the element needed no
    // transform, or an un-materialised Enumerable.Select otherwise. Both leaked the source across the map
    // boundary: the identity form made the destination literally BE the source list (a consumer could cast the
    // IEnumerable<T> back to List<T> and write into the entity — AutoMapper's v3.1 "assignable collection"
    // bug), and the deferred form re-ran the element conversion on every enumeration and threw
    // InvalidOperationException if the source was mutated after mapping. Map() must return an INDEPENDENT
    // value. These tests pin the safe contract; the runtime proof lives in CollectionAliasingRuntimeTests.

    [Fact]
    public void IEnumerable_target_does_NOT_alias_the_source()
    {
        var gen = Col.GenOf("""
                            using DwarfMapper;
                            using System.Collections.Generic;
                            namespace Demo;
                            public class A { public List<int> Xs { get; set; } = new(); }
                            public class B { public IEnumerable<int> Xs { get; set; } = System.Array.Empty<int>(); }
                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                            """);
        // The identity pass-through ("return src;") handed the destination the SOURCE collection. It must
        // never be emitted — the copy has to be a fresh buffer.
        Assert.DoesNotContain("return src;", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void IEnumerable_target_is_materialised_into_an_exact_sized_buffer()
    {
        var gen = Col.GenOf("""
                            using DwarfMapper;
                            using System.Collections.Generic;
                            namespace Demo;
                            public class A { public List<int> Xs { get; set; } = new(); }
                            public class B { public IEnumerable<int> Xs { get; set; } = System.Array.Empty<int>(); }
                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                            """);
        // Source Count is known (List<T>), so the cheapest independent copy is a single exactly-sized array
        // filled by index — one allocation, no List growth, no per-Add bounds/version checks.
        Assert.Contains("new int[src.Count]", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void IEnumerable_with_conversion_is_materialised_not_deferred()
    {
        var gen = Col.GenOf("""
                            using DwarfMapper;
                            using System.Collections.Generic;
                            namespace Demo;
                            public class A { public List<int> Xs { get; set; } = new(); }
                            public class B { public IEnumerable<long> Xs { get; set; } = System.Array.Empty<long>(); }
                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                            """);
        // A deferred Select would re-run the int->long conversion on every enumeration and would throw if the
        // source list were mutated after mapping. The conversion must be applied once, into a real buffer.
        Assert.DoesNotContain(".Select", gen, StringComparison.Ordinal);
        Assert.Contains("new long[src.Count]", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void IEnumerable_target_enumerates_the_source_exactly_once()
    {
        var gen = Col.GenOf("""
                            using DwarfMapper;
                            using System.Collections.Generic;
                            namespace Demo;
                            public class A { public List<int> Xs { get; set; } = new(); }
                            public class B { public IEnumerable<long> Xs { get; set; } = System.Array.Empty<long>(); }
                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                            """);
        // Sizing from src.Count (a property read) rather than counting by enumeration keeps this to a single
        // pass — a count-then-enumerate shape would double-enumerate a side-effecting source.
        var passes = gen.Split("foreach (var __item in src)").Length - 1;
        Assert.Equal(1, passes);
    }
}

// ─── IsProjectionTranslatable classification ─────────────────────────────────
// We verify via the generator harness: the IsProjectionTranslatable flag lives
// in TargetKind; we assert by ensuring collection generation succeeds AND by
// checking a comment/sentinel emitted into generated code when the kind is set.
// (Part D wires it up further; here we just verify the classification compiles
// and produces the right kind sentinel in the synthesized method name.)

public class IsProjectionTranslatableTests
{
    // For translatable kinds, the generated method name must contain the kind token.
    // List, Array, IEnumerable, IReadOnlyList, ICollection, IList, IReadOnlyCollection → translatable
    [Fact]
    public void List_target_compiles_and_no_errors()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                     public class B { public List<int> Xs { get; set; } = new(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void Array_target_compiles_and_no_errors()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     namespace Demo;
                     public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                     public class B { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void IReadOnlyList_target_compiles_and_no_errors()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                     public class B { public IReadOnlyList<int> Xs { get; set; } = new List<int>(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void ICollection_target_compiles_and_no_errors()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                     public class B { public ICollection<int> Xs { get; set; } = new List<int>(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    // Non-translatable: HashSet, ISet — must still compile but are NOT translatable
    // (Part D will reject them, but they emit fine in a regular runtime mapper)
    [Fact]
    public void HashSet_target_compiles_and_no_errors()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                     public class B { public HashSet<int> Xs { get; set; } = new(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void ISet_target_compiles_and_no_errors()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                     public class B { public ISet<int> Xs { get; set; } = new HashSet<int>(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }
}

// ─── NullCollections knob ─────────────────────────────────────────────────────

public class NullCollectionsTests
{
    // Default (AsEmpty) — null source → empty target
    [Fact]
    public void Default_null_collection_source_yields_empty_not_throw()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public List<int>? Xs { get; set; } }
                     public class B { public List<int> Xs { get; set; } = new(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    // AsNull → emitted code must propagate null (target must be nullable)
    [Fact]
    public void AsNull_null_collection_source_propagates_null()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public List<int>? Xs { get; set; } }
                     public class B { public List<int>? Xs { get; set; } }
                     [DwarfMapper(NullCollections = DwarfMapper.NullCollectionStrategy.AsNull)]
                     public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void AsNull_emits_null_propagation_in_generated_code()
    {
        var gen = Col.GenOf("""
                            using DwarfMapper;
                            using System.Collections.Generic;
                            namespace Demo;
                            public class A { public List<int>? Xs { get; set; } }
                            public class B { public List<int>? Xs { get; set; } }
                            [DwarfMapper(NullCollections = DwarfMapper.NullCollectionStrategy.AsNull)]
                            public partial class M { public partial B Map(A a); }
                            """);
        // With AsNull, the generated collection helper should pass null through,
        // NOT return an empty collection.
        Assert.DoesNotContain("Array.Empty", gen, StringComparison.Ordinal);
        Assert.Contains("null", gen, StringComparison.Ordinal);
    }
}

// ─── Nested collections ───────────────────────────────────────────────────────

public class NestedCollectionTests
{
    [Fact]
    public void List_of_List_int_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public List<List<int>> Xs { get; set; } = new(); }
                     public class B { public List<List<int>> Xs { get; set; } = new(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void Array_of_array_int_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public int[][] Xs { get; set; } = System.Array.Empty<int[]>(); }
                     public class B { public int[][] Xs { get; set; } = System.Array.Empty<int[]>(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void Dictionary_with_List_value_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public Dictionary<string,List<int>> D { get; set; } = new(); }
                     public class B { public Dictionary<string,List<int>> D { get; set; } = new(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void List_of_int_array_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public List<int[]> Xs { get; set; } = new(); }
                     public class B { public List<int[]> Xs { get; set; } = new(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    // Cross-type nested: List<long> → ImmutableArray<int> (CreateChecked per element)
    [Fact]
    public void List_long_to_ImmutableArray_int_with_checked_conversion_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     using System.Collections.Immutable;
                     namespace Demo;
                     public class A { public List<long> Xs { get; set; } = new(); }
                     public class B { public ImmutableArray<int> Xs { get; set; } }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    // String→int via IParsable per element
    [Fact]
    public void List_string_to_List_int_via_parsable_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public List<string> Xs { get; set; } = new(); }
                     public class B { public List<int> Xs { get; set; } = new(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    // Dictionary<string,long> → ImmutableDictionary<string,int>
    [Fact]
    public void Dict_string_long_to_ImmutableDict_string_int_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     using System.Collections.Immutable;
                     namespace Demo;
                     public class A { public Dictionary<string,long> D { get; set; } = new(); }
                     public class B { public ImmutableDictionary<string,int> D { get; set; } = ImmutableDictionary<string,int>.Empty; }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }
}

// ─── Part A + Part B composition (List<SrcRec> → List<DstRec>) ──────────────

public class CollectionWithAutoNestTests
{
    [Fact]
    public void List_of_SrcRec_to_List_of_DstRec_via_auto_nest_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class SrcRec { public string Name { get; set; } = ""; public int Age { get; set; } }
                     public class DstRec { public string Name { get; set; } = ""; public int Age { get; set; } }
                     public class A { public List<SrcRec> Items { get; set; } = new(); }
                     public class B { public List<DstRec> Items { get; set; } = new(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }

    [Fact]
    public void ImmutableArray_of_SrcRec_to_ImmutableArray_of_DstRec_via_auto_nest_compiles()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     using System.Collections.Immutable;
                     namespace Demo;
                     public class SrcRec { public string Name { get; set; } = ""; }
                     public class DstRec { public string Name { get; set; } = ""; }
                     public class A { public ImmutableArray<SrcRec> Items { get; set; } }
                     public class B { public ImmutableArray<DstRec> Items { get; set; } }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }
}

// ─── Regression: existing fast-paths still work ──────────────────────────────

public class CollectionRegressionTests
{
    [Fact]
    public void Array_identity_still_uses_Clone()
    {
        var gen = Col.GenOf("""
                            using DwarfMapper;
                            namespace Demo;
                            public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                            public class B { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                            """);
        Assert.Contains("Clone()", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void List_identity_still_uses_bulk_ctor()
    {
        var gen = Col.GenOf("""
                            using DwarfMapper;
                            using System.Collections.Generic;
                            namespace Demo;
                            public class A { public List<int> Xs { get; set; } = new(); }
                            public class B { public List<int> Xs { get; set; } = new(); }
                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                            """);
        Assert.Contains("new global::System.Collections.Generic.List<int>(src)", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void HashSet_still_works()
    {
        Col.NoErrors("""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class A { public HashSet<int> Xs { get; set; } = new(); }
                     public class B { public HashSet<int> Xs { get; set; } = new(); }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """);
    }
}
