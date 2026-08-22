// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Globalization;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     <b>I19.</b> <c>NullCollections</c> is a documented option of the mapper, not of one endpoint: a null
///     source collection must produce the same destination value through <c>.Project</c> as it does through
///     <c>.Map</c>. The projection pipeline used to not read the option at all — <c>Map</c> emitted the
///     documented <c>AsEmpty</c> helper (<c>src is null ? new List&lt;T&gt;() : …</c>) and <c>Project</c>
///     emitted <c>__s.M == null ? null : …</c>, so the same member answered differently depending on which
///     method the caller reached for.
///     <para>
///         <b>The table is option × target-nullability × target-kind, and every cell executes BOTH
///         endpoints</b> — the assertion is always "Project matches the documented value" AND "Map matches
///         it too", because agreement alone is satisfied by both endpoints being wrong together.
///     </para>
///     <para>
///         <b>The controls.</b> <c>AsNull</c> over a NON-nullable target is the documented degrade ("a
///         non-nullable target silently degrades to <c>AsEmpty</c>", <c>docs/options.md</c>) and is in the
///         table so a fix that reads the option but forgets the capability check cannot pass; the
///         <c>AsNull</c>-over-nullable cells are the ones that must still propagate null, so a fix that
///         hard-wired "always empty" cannot pass either. Every cell also carries a NON-null source row, so a
///         fix that returns empty for everything fails on the value.
///     </para>
///     <para>
///         <b>Target kind matters and is not decoration.</b> The empty arm of the emitted conditional is
///         chosen per translatable target kind so both arms share one static type and no cast is needed:
///         <c>Array.Empty&lt;T&gt;()</c> against <c>ToArray</c>, <c>Enumerable.Empty&lt;T&gt;()</c> against
///         the lazy <c>Select</c>, <c>new List&lt;T&gt;()</c> against <c>ToList</c>. A single-kind test
///         would have proven one third of that.
///     </para>
///     <para>
///         <b>Stated limit (B19's rule).</b> The projection runs over LINQ-to-Objects
///         (<c>AsQueryable()</c>): that proves the emitted tree COMPILES and EVALUATES to the documented
///         value. It does not prove any ORM translates it — no provider runs here. What bounds the claim is
///         that no ternary is added where none existed: this endpoint already emitted
///         <c>__s.M == null ? … : …</c> for every source member that may be null, and only the null arm's
///         value changed.
///     </para>
///     <para>
///         <b>The residual bound, ruled rather than overlooked.</b> The guard is still emitted only when the
///         source member MAY be null (nullable-annotated or nullable-oblivious), which is what keeps a
///         cleanly-annotated consumer's query free of a construct its provider may not translate. A
///         <c>#nullable</c>-enabled consumer whose NON-nullable collection member is null at runtime has
///         violated its own annotation, and gets an exception from <c>Project</c> where <c>Map</c> returns
///         empty — unchanged by I19, and pinned below so it is a bound and not a surprise.
///     </para>
/// </summary>
public class ProjectionNullCollectionsTests
{
    /// <summary>option, target-nullable, target kind, expect-null (else expect-empty).</summary>
    public static TheoryData<string, bool, string, bool> Cells()
    {
        var data = new TheoryData<string, bool, string, bool>();
        foreach (var kind in new[] { "List<int>", "int[]", "IEnumerable<int>", "IReadOnlyList<int>" })
        {
            // AsEmpty — the DEFAULT, and the cell I19 was filed on. Empty either way: in this direction the
            // option does not consult the target's nullability at all.
            data.Add("", false, kind, false);
            data.Add("", true, kind, false);
            // AsNull over a target that CAN hold the null — the one cell that must still yield null, and
            // the reason the fix is "read the option", not "always materialise".
            data.Add("(NullCollections = NullCollectionStrategy.AsNull)", true, kind, true);
            // CONTROL: AsNull over a target that CANNOT hold the null — the documented degrade to AsEmpty,
            // computed by the same predicate the runtime endpoint uses.
            data.Add("(NullCollections = NullCollectionStrategy.AsNull)", false, kind, false);
        }

        return data;
    }

    private static string Source(string option, bool targetNullable, string kind)
    {
        var q = targetNullable ? "?" : "";
        return $$"""
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            using DwarfMapper;
            namespace Demo;

            public sealed class Src { public {{kind}}? M { get; set; } }
            public sealed class Dst { public {{kind}}{{q}} M { get; set; } = default!; }

            [DwarfMapper{{option}}]
            public partial class NcMapper
            {
                public partial Dst Map(Src s);
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void Project_honours_NullCollections_exactly_as_Map_does(
        string option, bool targetNullable, string kind, bool expectNull)
    {
        var (assembly, errors) = GeneratorTestHarness.EmitAssembly(Source(option, targetNullable, kind));
        Assert.True(assembly is not null,
            $"[{option}|nullable={targetNullable}|{kind}] did not emit: "
            + string.Join(", ", errors.Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture))));

        var srcType = assembly!.GetType("Demo.Src")!;
        var mapperType = assembly.GetType("Demo.NcMapper")!;
        var mapper = Activator.CreateInstance(mapperType)!;
        var member = srcType.GetProperty("M")!;

        var nullSource = Activator.CreateInstance(srcType)!; // M left null — the whole input
        var valueSource = Activator.CreateInstance(srcType)!;
        member.SetValue(valueSource, Materialise(kind, [7, 9]));

        // ── .Map: the reference. K1 holds it to the naive oracle, so it is the endpoint to agree WITH. ──
        var map = mapperType.GetMethod("Map")!;
        var mappedNull = Member(map.Invoke(mapper, [nullSource])!);
        var mappedValue = Member(map.Invoke(mapper, [valueSource])!);

        // ── .Project: the SAME two inputs, in ONE query, so cardinality is asserted with the values. ──
        var listType = typeof(List<>).MakeGenericType(srcType);
        var list = (IList)Activator.CreateInstance(listType)!;
        list.Add(nullSource);
        list.Add(valueSource);
        var queryable = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.AsQueryable) && m.IsGenericMethodDefinition)
            .MakeGenericMethod(srcType)
            .Invoke(null, [list])!;
        var projected = ((IQueryable)mapperType.GetMethod("Project")!.Invoke(mapper, [queryable])!)
            .Cast<object>()
            .ToList();
        Assert.Equal(2, projected.Count);
        var projectedNull = Member(projected[0]);
        var projectedValue = Member(projected[1]);

        var cell = $"[NullCollections{(string.IsNullOrEmpty(option) ? "=AsEmpty (default)" : option)}"
                   + $" | target {(targetNullable ? "nullable" : "non-nullable")} | {kind}] ";

        // The documented value first, so a failure reads "the product is wrong" rather than "the two agree".
        if (expectNull)
        {
            Assert.True(mappedNull is null, cell + "Map did not propagate the null under AsNull.");
            Assert.True(projectedNull is null,
                cell + "Project did not propagate the null under AsNull over a null-capable target — it "
                + "produced " + Describe(projectedNull) + ". Reading NullCollections is not enough: the "
                + "AsNull branch has to survive the read.");
        }
        else
        {
            Assert.True(mappedNull is not null, cell + "Map did not materialise the documented empty.");
            Assert.Empty(((IEnumerable)mappedNull!).Cast<object?>());
            Assert.True(projectedNull is not null,
                cell + "I19: Project returned null for a null source collection where the documented "
                + "behaviour is an EMPTY one (AsEmpty, or AsNull degraded because the target cannot hold "
                + "the null). Map returned " + Describe(mappedNull) + ".");
            Assert.Empty(((IEnumerable)projectedNull!).Cast<object?>());
        }

        // The non-null direction, in every cell: an "always empty" or "always null" fix dies here.
        Assert.Equal([7, 9], ((IEnumerable)mappedValue!).Cast<int>());
        Assert.Equal([7, 9], ((IEnumerable)projectedValue!).Cast<int>());

        object? Member(object dst) => dst.GetType().GetProperty("M")!.GetValue(dst);
    }

    /// <summary>
    ///     The RULED BOUND of the fix, pinned so it cannot become an accident. The projection's null guard
    ///     is emitted only for a source member that MAY be null; a <c>#nullable</c>-enabled consumer whose
    ///     NON-nullable member is null anyway has violated its own annotation, and the two endpoints part
    ///     company there — <c>Map</c> is annotation-blind at runtime and returns empty, <c>Project</c> has
    ///     no guard to run.
    ///     <para>
    ///         <b>Why that bound and not the obvious alternative.</b> Guarding unconditionally would close
    ///         it, and would insert a null-check ternary into EVERY correctly-annotated projection — the one
    ///         change here that could stop a consumer's query translating today. The narrower rule adds no
    ///         construct to any query that did not already have one. This test is what makes widening the
    ///         rule later a decision rather than a drift.
    ///     </para>
    /// </summary>
    [Fact]
    public void A_non_nullable_annotated_source_collection_gets_no_guard_and_that_is_the_bound()
    {
        const string code = """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            using DwarfMapper;
            namespace Demo;

            public sealed class Src { public List<int> M { get; set; } = new(); }
            public sealed class Dst { public List<int> M { get; set; } = new(); }

            [DwarfMapper]
            public partial class NcMapper
            {
                public partial Dst Map(Src s);
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """;

        var (assembly, errors) = GeneratorTestHarness.EmitAssembly(code);
        Assert.True(assembly is not null,
            string.Join(", ", errors.Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture))));

        var srcType = assembly!.GetType("Demo.Src")!;
        var mapperType = assembly.GetType("Demo.NcMapper")!;
        var mapper = Activator.CreateInstance(mapperType)!;
        var source = Activator.CreateInstance(srcType)!;
        srcType.GetProperty("M")!.SetValue(source, null); // violates the annotation, on purpose

        // Map is annotation-blind at runtime: the emitted helper always tests `src is null`.
        var mapped = mapperType.GetMethod("Map")!.Invoke(mapper, [source])!;
        Assert.Empty((IEnumerable)mapped.GetType().GetProperty("M")!.GetValue(mapped)!);

        // Project has no guard to run, because the consumer declared the member never-null.
        var listType = typeof(List<>).MakeGenericType(srcType);
        var list = (IList)Activator.CreateInstance(listType)!;
        list.Add(source);
        var queryable = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.AsQueryable) && m.IsGenericMethodDefinition)
            .MakeGenericMethod(srcType)
            .Invoke(null, [list])!;
        var projection = (IQueryable)mapperType.GetMethod("Project")!.Invoke(mapper, [queryable])!;

        Assert.ThrowsAny<Exception>(() => projection.Cast<object>().ToList());
    }

    private static object Materialise(string kind, int[] values) => kind switch
    {
        "int[]" => values,
        _ => new List<int>(values)
    };

    private static string Describe(object? value) =>
        value is null
            ? "null"
            : value.GetType().Name + " with "
              + ((IEnumerable)value).Cast<object?>().Count().ToString(CultureInfo.InvariantCulture)
              + " element(s)";
}
