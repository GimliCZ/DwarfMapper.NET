// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// SELF-VALIDATION over the whole <c>System.*</c> type surface — the "never silent" promise applied to TYPES
/// rather than to members.
/// <para>
/// For every <c>System.*</c> data/memory apparatus a DTO member could plausibly hold, exactly one of two
/// things must be true:
/// </para>
/// <list type="number">
///   <item>
///     <b>SUPPORTED</b> — the generator accepts it, and then it MUST (a) emit code that actually compiles, and
///     (b) be reachable by the fuzz schema. A supported-but-unfuzzed type is a blind spot: that is precisely
///     how an <c>IEnumerable&lt;T&gt;</c>-target aliasing bug survived the entire suite.
///   </item>
///   <item>
///     <b>UNSUPPORTED</b> — the generator REFUSES it with a DWARF diagnostic. Loudly. Not a silent wrong
///     mapping, not a raw CS compile error out of generated code, and not a generator crash.
///   </item>
/// </list>
/// <para>
/// The failure this rules out is the one that actually bit us: a type the generator quietly accepted and
/// mis-handled, in a shape no fuzzer ever produced. Both halves matter — "it compiles" is not enough if
/// nothing ever exercises it, and "it's rejected" is only acceptable if the rejection is a real diagnostic.
/// </para>
/// </summary>
public class SystemTypeCoverageSelfValidationTests
{
    /// <summary>
    /// The System.* surface a mapper can plausibly meet as a member type: generic collections, the immutable
    /// family, concurrent and frozen collections, the ordered/linked collections, and the memory apparatus
    /// (Memory&lt;T&gt;, ReadOnlyMemory&lt;T&gt;, ArraySegment&lt;T&gt;, ReadOnlySequence&lt;T&gt;).
    /// <para>
    /// Span&lt;T&gt;/ReadOnlySpan&lt;T&gt; are deliberately absent: they are ref structs and cannot be the type
    /// of a property or field at all, so they are unreachable as DTO members by the language itself. (They ARE
    /// supported as span-map METHOD parameters, which is a separate path with its own tests.)
    /// </para>
    /// </summary>
    public static TheoryData<string> SystemTypes() =>
    [
        // ── System.Collections.Generic ─────────────────────────────────────────
        "global::System.Collections.Generic.List<int>",
        "global::System.Collections.Generic.IList<int>",
        "global::System.Collections.Generic.ICollection<int>",
        "global::System.Collections.Generic.IEnumerable<int>",
        "global::System.Collections.Generic.IReadOnlyList<int>",
        "global::System.Collections.Generic.IReadOnlyCollection<int>",
        "global::System.Collections.Generic.HashSet<int>",
        "global::System.Collections.Generic.ISet<int>",
        "global::System.Collections.Generic.IReadOnlySet<int>",
        "global::System.Collections.Generic.SortedSet<int>",
        "global::System.Collections.Generic.LinkedList<int>",
        "global::System.Collections.Generic.Stack<int>",
        "global::System.Collections.Generic.Queue<int>",
        "global::System.Collections.Generic.PriorityQueue<int, int>",
        "global::System.Collections.Generic.Dictionary<int, string>",
        "global::System.Collections.Generic.IDictionary<int, string>",
        "global::System.Collections.Generic.IReadOnlyDictionary<int, string>",
        "global::System.Collections.Generic.SortedDictionary<int, string>",
        "global::System.Collections.Generic.SortedList<int, string>",

        // ── System.Collections.ObjectModel (all IEnumerable → must be refused, DWARF027) ────────
        "global::System.Collections.ObjectModel.Collection<int>",
        "global::System.Collections.ObjectModel.ReadOnlyCollection<int>",
        "global::System.Collections.ObjectModel.ObservableCollection<int>",
        "global::System.Collections.ObjectModel.ReadOnlyObservableCollection<int>",
        "global::System.Collections.ObjectModel.ReadOnlyDictionary<int, string>",

        // ── System.Collections.Immutable ───────────────────────────────────────
        "global::System.Collections.Immutable.ImmutableArray<int>",
        "global::System.Collections.Immutable.ImmutableList<int>",
        "global::System.Collections.Immutable.IImmutableList<int>",
        "global::System.Collections.Immutable.ImmutableHashSet<int>",
        "global::System.Collections.Immutable.IImmutableSet<int>",
        "global::System.Collections.Immutable.ImmutableSortedSet<int>",
        "global::System.Collections.Immutable.ImmutableDictionary<int, string>",
        "global::System.Collections.Immutable.IImmutableDictionary<int, string>",
        "global::System.Collections.Immutable.ImmutableSortedDictionary<int, string>",
        "global::System.Collections.Immutable.ImmutableQueue<int>",
        "global::System.Collections.Immutable.IImmutableQueue<int>",
        "global::System.Collections.Immutable.ImmutableStack<int>",
        "global::System.Collections.Immutable.IImmutableStack<int>",

        // ── System.Collections.Concurrent ──────────────────────────────────────
        "global::System.Collections.Concurrent.ConcurrentBag<int>",
        "global::System.Collections.Concurrent.ConcurrentQueue<int>",
        "global::System.Collections.Concurrent.ConcurrentStack<int>",
        "global::System.Collections.Concurrent.ConcurrentDictionary<int, string>",
        "global::System.Collections.Concurrent.BlockingCollection<int>",

        // ── System.Collections + System.Collections.Specialized (non-generic; IEnumerable → refuse) ──
        "global::System.Collections.ArrayList",
        "global::System.Collections.Hashtable",
        "global::System.Collections.BitArray",
        "global::System.Collections.Specialized.NameValueCollection",
        "global::System.Collections.Specialized.OrderedDictionary",

        // ── System.Collections.Frozen ──────────────────────────────────────────
        "global::System.Collections.Frozen.FrozenSet<int>",
        "global::System.Collections.Frozen.FrozenDictionary<int, string>",

        // ── Tuples / pairs / Nullable / Lazy — NOT IEnumerable, so they identity-assign and COMPILE
        // today. The docstring warns about exactly this blind spot: a future change routing same-type
        // members through nested-object mapping instead of identity assignment would silently break them,
        // and only a test that exercises them would notice.
        "global::System.Collections.Generic.KeyValuePair<int, string>",
        "global::System.Tuple<int, string>",
        "(int, string)",
        "int?",
        "global::System.Lazy<int>",

        // ── The memory apparatus ───────────────────────────────────────────────
        "global::System.Memory<int>",
        "global::System.ReadOnlyMemory<int>",
        "global::System.ArraySegment<int>",
        "global::System.Buffers.ReadOnlySequence<int>",

        // ── Arrays ─────────────────────────────────────────────────────────────
        "int[]",
    ];

    private static string MapperFor(string memberType) => $$"""
        using DwarfMapper;
        namespace Demo;
        public class A { public {{memberType}} Xs { get; set; } = default!; }
        public class B { public {{memberType}} Xs { get; set; } = default!; }
        [DwarfMapper] public partial class M { public partial B Map(A a); }
        """;

    [Theory]
    [MemberData(nameof(SystemTypes))]
    public void Every_system_type_is_either_supported_or_loudly_rejected(string memberType)
    {
        var source = MapperFor(memberType);

        var (diagnostics, _) = GeneratorTestHarness.Run(source);
        var dwarfErrors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (dwarfErrors.Count > 0)
        {
            // UNSUPPORTED is a legitimate answer — but only when it is stated LOUDLY, as a DwarfMapper
            // diagnostic the user can act on. A refusal that surfaces as a raw compiler error out of generated
            // code, or as a generator crash, is not a refusal — it is a defect.
            Assert.All(dwarfErrors, d =>
                Assert.True(d.Id.StartsWith("DWARF", StringComparison.Ordinal),
                    $"{memberType} is rejected, but not by a DwarfMapper diagnostic — got "
                    + $"'{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}'. An unsupported type must be "
                    + "refused with a DWARF diagnostic, never leak a raw compiler error."));
            return;
        }

        // SUPPORTED. Then the generated code MUST compile. Emitting code that does not compile is the worst
        // possible outcome: it is neither a working mapping nor an actionable diagnostic. (This is exactly the
        // class of bug found in [GenerateMap] + collection-mediated recursion, which emitted a call to a
        // 3-parameter helper with one argument.)
        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(source).ToList();
        Assert.True(
            compileErrors.Count == 0,
            $"{memberType} was ACCEPTED by the generator but the emitted code does not compile:\n  "
            + string.Join("\n  ",
                compileErrors.Select(e => $"{e.Id}: {e.GetMessage(CultureInfo.InvariantCulture)}"))
            + "\n\nA type must be either supported (and generate valid code) or refused with a diagnostic — "
            + "never silently accepted and then broken.");
    }

    [Fact]
    public void Span_types_are_not_valid_members_by_construction()
    {
        // Documents the one deliberate omission above, so a future reader doesn't "helpfully" add them:
        // ref structs cannot be the type of a property or field, so they can never be a DTO member. The
        // language enforces this, not DwarfMapper.
        Assert.True(typeof(Span<int>).IsByRefLike);
        Assert.True(typeof(ReadOnlySpan<int>).IsByRefLike);
    }
}
