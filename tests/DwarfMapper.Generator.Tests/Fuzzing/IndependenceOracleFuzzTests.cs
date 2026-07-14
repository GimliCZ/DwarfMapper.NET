// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using DwarfMapper.Testing;

namespace DwarfMapper.Generator.Tests.Fuzzing;

/// <summary>
/// Property: <b>Map() returns an INDEPENDENT value</b> — the mapped graph shares no mutable collection
/// instance with the source graph.
/// <para>
/// This property was missing, and its absence let a real bug through: an <c>IEnumerable&lt;T&gt;</c>
/// destination member was handed the source's own <c>List&lt;T&gt;</c>. Every existing oracle passed, because
/// an aliased collection has exactly the right VALUES — <see cref="CrossTypeComparer" /> compares values, and
/// the topology oracles compare identity WITHIN a single graph. Nothing looked across the map boundary. A
/// value-based oracle is structurally incapable of catching aliasing; only a reference-identity one can.
/// </para>
/// <para>
/// It is worth stating what this buys beyond the one bug: aliasing is a whole CLASS of defect (a future
/// "assignable target" or "same element type" fast-path is exactly the optimisation that reintroduces it),
/// and this property catches any of them, on every shape the schema can generate, without anyone thinking to
/// write a case for it.
/// </para>
/// </summary>
public class IndependenceOracleFuzzTests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(0, 60).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Mapped_graph_shares_no_collection_instance_with_the_source(int seed)
    {
        var src = SyntheticSchema.GenerateBehavioral(seed);
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(src);
        Assert.True(asm is not null,
            $"seed={seed} failed to emit: {string.Join(", ", errors.Select(e => e.Id))}\n{src}");

        var srcType = asm!.GetType("Fuzz.Src")!;
        var mapperType = asm.GetType("Fuzz.FuzzMapper")!;
        var mapper = Activator.CreateInstance(mapperType)!;
        var map = mapperType.GetMethod("Map")!;

        var original = ObjectFactory.Create(srcType, new Random(seed), 0)!;
        var mapped = map.Invoke(mapper, new[] { original })!;

        var shared = IndependenceOracle.SharedCollections(original, mapped);

        Assert.True(
            shared.Count == 0,
            $"seed={seed}: the mapped graph SHARES {shared.Count} collection instance(s) with the source — "
            + $"mutating either side silently corrupts the other.\n{IndependenceOracle.Render(shared)}"
            + $"--- source ---\n{src}");
    }

    [Fact]
    public void The_oracle_actually_detects_aliasing()
    {
        // Guard the guard: an oracle that can never fail is worthless. Hand it a deliberately aliased pair and
        // confirm it fires — otherwise a silently-broken walker would make every test above vacuously green.
        var shared = new List<int> { 1, 2, 3 };
        var source = new AliasProbe { Items = shared };
        var aliased = new AliasProbe { Items = shared }; // same instance: this is the bug shape
        var copied = new AliasProbe { Items = new List<int> { 1, 2, 3 } }; // equal values, different instance

        Assert.Single(IndependenceOracle.SharedCollections(source, aliased));
        Assert.Empty(IndependenceOracle.SharedCollections(source, copied));
    }

    private sealed class AliasProbe
    {
        public List<int> Items { get; set; } = new();
    }
}
