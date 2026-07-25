// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     A collection member whose SOURCE type is a value type — <c>ImmutableArray&lt;T&gt;</c> being the one that
///     occurs in practice.
///     <para>
///         The synthesized helper takes its source parameter as <c>Nullable&lt;TSource&gt;</c> (every collection
///         helper is null-tolerant), and <c>Nullable&lt;T&gt;</c> implements neither <c>IEnumerable&lt;T&gt;</c>
///         nor <c>Count</c>/<c>Length</c>. Every emitter that enumerated or counted <c>src</c> directly therefore
///         produced code that did not compile — <c>CS0411</c> from <c>CreateRange(src)</c> /
///         <c>TryGetNonEnumeratedCount(src)</c>, and <c>CS1061</c> from <c>src.Count</c>.
///     </para>
///     <para>
///         The whole class of bug hid behind a coverage hole: every pre-existing immutable/collection test used
///         <c>List&lt;int&gt;</c> — a reference type — as the source, so the nullable-struct parameter was never
///         exercised. These tests pin the source side specifically, across every target family that has its own
///         emitter, so a future emitter that forgets the unwrap fails here rather than in a consumer's build.
///     </para>
/// </summary>
public class ValueTypeSourceCollectionTests
{
    private static string Source(string targetType) => $$"""
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using DwarfMapper;
        namespace Demo;

        public sealed class Src { public ImmutableArray<int> V { get; set; } }
        public sealed class Dst { public {{targetType}} V { get; set; } = default!; }

        [DwarfMapper]
        [GenerateMap<Src, Dst>]
        public partial class M { }
        """;

    [Theory]
    // Each target routes to a DIFFERENT emitter, and each one previously emitted uncompilable code.
    [InlineData("ImmutableList<int>")]      // EmitImmutableCollection  — CreateRange(src)          CS0411
    [InlineData("IImmutableList<int>")]     // EmitImmutableCollection
    [InlineData("ImmutableHashSet<int>")]   // EmitImmutableCollection
    [InlineData("IImmutableSet<int>")]      // EmitImmutableCollection
    [InlineData("int[]")]                   // EmitArray                — src.Count / TryGetNonEnumeratedCount
    [InlineData("List<int>")]               // EmitList                 — capacity arg src.Count    CS1061
    [InlineData("HashSet<int>")]            // EmitHashSet
    [InlineData("ISet<int>")]               // EmitHashSet
    [InlineData("IReadOnlyList<int>")]      // EmitList (interface target)
    [InlineData("IEnumerable<int>")]        // EmitLazyEnumerable
    [InlineData("Queue<int>")]              // EmitStackQueue
    [InlineData("Stack<int>")]              // EmitStackQueue           — reversed source enumeration
    [InlineData("ImmutableArray<int>")]     // EmitImmutableArray       — already had the unwrap; guards it
    public void ImmutableArray_source_emits_compilable_code(string targetType)
    {
        // EmitsCompilableCode fails on ANY compiler diagnostic in the generated tree, which is exactly the
        // CS0411/CS1061 this regression is about.
        GeneratorAssert.EmitsCompilableCode(Source(targetType));
    }
}
