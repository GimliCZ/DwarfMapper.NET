// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Globalization;
using System.Reflection;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     I14, the projection half of I5/I7: a nullable nested member <c>S1? M → D1? M</c> must lift
///     <c>null → null</c> through <c>.Project</c> for EVERY combination of source and destination kind, and
///     must do it in agreement with <c>.Map</c> on the same mapper.
///     <para>
///         The projection engine carries its own nullable logic and did not share the resolver gate I5/I7
///         widened. It asked the destination's KIND — is it a <c>Nullable&lt;U&gt;</c>? — where the only
///         question that matters is whether the destination can HOLD the null. So the two diagonals worked,
///         the four cross-kind cells were refused with <c>DWARF028</c>, and because that is an Error the whole
///         mapper generated nothing: the consumer lost the <c>Map</c> method too.
///     </para>
///     <para>
///         <b>The diagonals are in the table as CONTROLS.</b> Struct→Struct and Class→Class always worked; a
///         "fix" that lifted the four cross-kind cells by breaking the two diagonals would pass a four-cell
///         test. Every cell also asserts the NON-null direction, so a lift implemented by dropping the value
///         cannot pass either.
///     </para>
///     <para>
///         <b>Stated limit (B19's rule).</b> The projection is executed over LINQ-to-Objects
///         (<c>AsQueryable()</c>), which proves the emitted expression tree COMPILES and EVALUATES to the
///         right answer. It does not prove any particular ORM translates it to SQL — no provider runs here.
///         What makes that a bounded claim rather than a hopeful one is that the emitted forms are the same
///         two the diagonals have always emitted (a <c>HasValue</c> ternary and a <c>== null</c> ternary over
///         a member-init), recombined; no new construct enters the tree. <c>ProjectionRuntimeTests</c> in the
///         integration suite is the other end of the same rope.
///     </para>
/// </summary>
public class ProjectionNullableKindPairTests
{
    /// <summary>The six kind pairs I7 filed, diagonals first so a failure reads controls-then-subjects.</summary>
    public static TheoryData<string, string> KindPairs() => new()
    {
        { "struct", "struct" },              // control — always lifted
        { "class", "class" },                // control — always lifted
        { "struct", "class" },               // I14
        { "struct", "record" },              // I14
        { "recordstruct", "class" },         // I14
        { "class", "struct" },               // I14, the reverse genre
    };

    private static string Declare(string kind, string name) => kind switch
    {
        "struct" => $"public struct {name} {{ public int V {{ get; set; }} }}",
        "class" => $"public class {name} {{ public int V {{ get; set; }} }}",
        "record" => $"public record {name} {{ public int V {{ get; set; }} }}",
        "recordstruct" => $"public record struct {name} {{ public int V {{ get; set; }} }}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown kind")
    };

    private static string Source(string srcKind, string dstKind) => $$"""
        using System.Linq;
        using DwarfMapper;
        namespace Demo;

        {{Declare(srcKind, "S1")}}
        {{Declare(dstKind, "D1")}}

        public sealed class Src { public S1? M { get; set; } }
        public sealed class Dst { public D1? M { get; set; } }

        [DwarfMapper]
        public partial class KindPairMapper
        {
            public partial Dst Map(Src s);
            public partial IQueryable<Dst> Project(IQueryable<Src> q);
        }
        """;

    [Theory]
    [MemberData(nameof(KindPairs))]
    public void Project_lifts_null_across_every_kind_pair_and_agrees_with_Map(string srcKind, string dstKind)
    {
        var code = Source(srcKind, dstKind);

        // The mapper declares BOTH methods, so "it emitted" is itself the I14 assertion: before the fix the
        // cross-kind cells reported DWARF028 and the whole class — Map included — generated nothing.
        var (assembly, errors) = GeneratorTestHarness.EmitAssembly(code);
        Assert.True(assembly is not null,
            $"{srcKind}->{dstKind} did not emit: "
            + string.Join(", ", errors.Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture))));

        var srcType = assembly!.GetType("Demo.Src")!;
        var s1Type = assembly.GetType("Demo.S1")!;
        var mapperType = assembly.GetType("Demo.KindPairMapper")!;
        var mapper = Activator.CreateInstance(mapperType)!;

        var nullSource = Activator.CreateInstance(srcType)!;
        var valueSource = Activator.CreateInstance(srcType)!;
        var inner = Activator.CreateInstance(s1Type)!;
        s1Type.GetProperty("V")!.SetValue(inner, 42);
        srcType.GetProperty("M")!.SetValue(valueSource, inner);

        // ── .Map: the baseline both endpoints must agree on ───────────────────────────────────────────
        var map = mapperType.GetMethod("Map")!;
        Assert.Null(NestedValue(map.Invoke(mapper, [nullSource])!));
        Assert.Equal(42, NestedValue(map.Invoke(mapper, [valueSource])!));

        // ── .Project: same two inputs, in one query, so cardinality is asserted too ───────────────────
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
        Assert.Null(NestedValue(projected[0]));
        Assert.Equal(42, NestedValue(projected[1]));
    }

    /// <summary>
    ///     I5's shape through the OTHER endpoint: a collection of the same nullable cross-kind element. It
    ///     reaches the widened gate by the element recursion, so a fix applied only to the plain member would
    ///     leave it refused — which is how it was found.
    /// </summary>
    [Theory]
    [InlineData("struct", "class")]
    [InlineData("class", "struct")]
    public void Project_lifts_a_null_ELEMENT_across_a_rekinded_pair(string srcKind, string dstKind)
    {
        var code = $$"""
            using System.Collections.Generic;
            using System.Linq;
            using DwarfMapper;
            namespace Demo;

            {{Declare(srcKind, "S1")}}
            {{Declare(dstKind, "D1")}}

            public sealed class Src { public List<S1?> Items { get; set; } = new(); }
            public sealed class Dst { public List<D1?> Items { get; set; } = new(); }

            [DwarfMapper]
            public partial class ElementKindPairMapper
            {
                public partial Dst Map(Src s);
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """;

        var (assembly, errors) = GeneratorTestHarness.EmitAssembly(code);
        Assert.True(assembly is not null,
            $"{srcKind}->{dstKind} elements did not emit: "
            + string.Join(", ", errors.Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture))));

        var srcType = assembly!.GetType("Demo.Src")!;
        var s1Type = assembly.GetType("Demo.S1")!;
        var mapperType = assembly.GetType("Demo.ElementKindPairMapper")!;
        var mapper = Activator.CreateInstance(mapperType)!;

        var source = Activator.CreateInstance(srcType)!;
        var items = (IList)srcType.GetProperty("Items")!.GetValue(source)!;
        items.Add(null);
        var inner = Activator.CreateInstance(s1Type)!;
        s1Type.GetProperty("V")!.SetValue(inner, 7);
        items.Add(inner);

        var listType = typeof(List<>).MakeGenericType(srcType);
        var list = (IList)Activator.CreateInstance(listType)!;
        list.Add(source);
        var queryable = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.AsQueryable) && m.IsGenericMethodDefinition)
            .MakeGenericMethod(srcType)
            .Invoke(null, [list])!;

        var projected = ((IQueryable)mapperType.GetMethod("Project")!.Invoke(mapper, [queryable])!)
            .Cast<object>()
            .Single();

        var outItems = ((IEnumerable)projected.GetType().GetProperty("Items")!.GetValue(projected)!)
            .Cast<object?>()
            .ToList();

        // The lift, the value, AND the cardinality: a silent drop would leave one element behind.
        Assert.Equal(2, outItems.Count);
        Assert.Null(outItems[0]);
        Assert.Equal(7, outItems[1]!.GetType().GetProperty("V")!.GetValue(outItems[1]));
    }

    /// <summary>
    ///     The other side of the ruling, and it is NOT a bug: a target that genuinely cannot hold the null is
    ///     still refused. <c>.Map</c> answers that link with <c>NullStrategy</c>, which has no expression-tree
    ///     form, so the two endpoints CANNOT agree and a build-time refusal is the honest split. Pinned so a
    ///     later widening of the gate does not quietly take it too.
    /// </summary>
    [Theory]
    [InlineData("public int? M { get; set; }", "public int M { get; set; }")]
    [InlineData("public S1? M { get; set; }", "public D1 M { get; set; } = new();")]
    public void Project_still_refuses_a_target_that_cannot_hold_the_null(string srcMember, string dstMember)
    {
        var code = $$"""
            using System.Linq;
            using DwarfMapper;
            namespace Demo;

            public struct S1 { public int V { get; set; } }
            public class D1 { public int V { get; set; } }

            public sealed class Src { {{srcMember}} }
            public sealed class Dst { {{dstMember}} }

            [DwarfMapper]
            public partial class RefusingMapper
            {
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """;

        var (diagnostics, _) = GeneratorTestHarness.Run(code);
        var refusal = Assert.Single(diagnostics.Where(d => d.Id == "DWARF028"));

        // The message must name the CAPABILITY, not the kind: the wording it replaced said "a non-nullable
        // target" of a target that was `D1?` and perfectly nullable — the same confusion I7 named.
        Assert.Contains("cannot hold null", refusal.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     The sibling the I14 hunt found: a nullable OBJECT source into a VALUE-type nested target. The
    ///     nested-object resolver guarded on whether the source could be null without asking whether the
    ///     target could hold the result, and emitted <c>x == null ? null : new NestedStruct { … }</c> —
    ///     <c>CS0037</c> in a file the consumer cannot edit, reported by nothing. The EmittedInvalidCode
    ///     genre, and no sampled cell reached it: the type-graph generators mirror kinds across a pair.
    /// </summary>
    [Fact]
    public void Project_refuses_rather_than_emitting_CS0037_for_a_value_type_nested_target()
    {
        const string code = """
            using System.Linq;
            using DwarfMapper;
            namespace Demo;

            public class S1 { public int V { get; set; } }
            public struct D1 { public int V { get; set; } }

            public sealed class Src { public S1? M { get; set; } }
            public sealed class Dst { public D1 M { get; set; } }

            [DwarfMapper]
            public partial class ValueTargetMapper
            {
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """;

        var (diagnostics, generated) = GeneratorTestHarness.Run(code);
        var refusal = Assert.Single(diagnostics.Where(d => d.Id == "DWARF028"));
        Assert.Contains("value-type target", refusal.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

        // Non-vacuity: the refusal must have replaced the emission, not sat beside it.
        Assert.DoesNotContain("== null ? null : new global::Demo.D1", generated, StringComparison.Ordinal);
    }

    private static object? Nested(object dto) => dto.GetType().GetProperty("M")!.GetValue(dto);

    private static int? NestedValue(object dto)
    {
        var nested = Nested(dto);
        return nested is null ? null : (int)nested.GetType().GetProperty("V")!.GetValue(nested)!;
    }
}
