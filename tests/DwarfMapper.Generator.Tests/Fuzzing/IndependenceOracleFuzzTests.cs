// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Testing;
using DwarfMapper.TestInfrastructure;
using Xunit.Sdk;

namespace DwarfMapper.Generator.Tests.Fuzzing
{
    /// <summary>
    ///     Property: <b>Map() returns an INDEPENDENT value</b> — the mapped graph shares no mutable collection
    ///     instance with the source graph.
    ///     <para>
    ///         This property was missing, and its absence let a real bug through: an <c>IEnumerable&lt;T&gt;</c>
    ///         destination member was handed the source's own <c>List&lt;T&gt;</c>. Every existing oracle passed, because
    ///         an aliased collection has exactly the right VALUES — <see cref="CrossTypeComparer" /> compares values, and
    ///         the topology oracles compare identity WITHIN a single graph. Nothing looked across the map boundary. A
    ///         value-based oracle is structurally incapable of catching aliasing; only a reference-identity one can.
    ///     </para>
    ///     <para>
    ///         It is worth stating what this buys beyond the one bug: aliasing is a whole CLASS of defect (a future
    ///         "assignable target" or "same element type" fast-path is exactly the optimisation that reintroduces it),
    ///         and this property catches any of them, on every shape the schema can generate, without anyone thinking to
    ///         write a case for it.
    ///     </para>
    /// </summary>
    public class IndependenceOracleFuzzTests
    {
        // Fast 60 / deep 600 — see DeepPopulation.IndependenceSeeds.
        public static IEnumerable<object[]> Seeds()
        {
            return Enumerable.Range(0, DeepTier.Count(DeepPopulation.IndependenceSeeds)).Select(i => new object[]
            {
                i
            });
        }

        [Theory]
        [MemberData(nameof(Seeds))]
        public void Mapped_graph_shares_no_collection_instance_with_the_source(int seed)
        {
            var src = SyntheticSchema.GenerateBehavioral(seed);
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(src);
            Assert.True(asm is not null,
                $"seed={seed} failed to emit: {string.Join(", ", errors.Select(e => e.Id))}\n{src}");

            var srcType = asm.GetType("Fuzz.Src")!;
            var mapperType = asm.GetType("Fuzz.FuzzMapper")!;
            var mapper = Activator.CreateInstance(mapperType)!;
            var map = mapperType.GetMethod("Map")!;

            var original = ObjectFactoryV2.Create(srcType, new Random(seed), 0)!;
            var mapped = map.Invoke(mapper,
                new[]
                {
                    original
                })!;

            var shared = IndependenceOracle.SharedCollections(original, mapped);

            Assert.True(
                shared.Count == 0,
                $"seed={seed}: the mapped graph SHARES {shared.Count} collection instance(s) with the source — " + $"mutating either side silently corrupts the other.\n{IndependenceOracle.Render(shared)}" + $"--- source ---\n{src}");
        }

        [Fact]
        public void The_oracle_actually_detects_aliasing()
        {
            // Guard the guard: an oracle that can never fail is worthless. Hand it a deliberately aliased pair and
            // confirm it fires — otherwise a silently-broken walker would make every test above vacuously green.
            var shared = new List<int>
            {
                1,
                2,
                3
            };
            var source = new AliasProbe
            {
                Items = shared
            };
            var aliased = new AliasProbe
            {
                Items = shared
            }; // same instance: this is the bug shape
            var copied = new AliasProbe
            {
                Items = new List<int>
                {
                    1,
                    2,
                    3
                }
            }; // equal values, different instance

            Assert.Single(IndependenceOracle.SharedCollections(source, aliased));
            Assert.Empty(IndependenceOracle.SharedCollections(source, copied));
        }

        /// <summary>
        ///     Round 34 (the silenced-checks inventory): the walk skipped a member whose getter threw and stopped at depth
        ///     32, returning what it had seen either way, so "shares no collection" could pass for parts of a graph it never
        ///     examined. Now the walk fails and names the part.
        /// </summary>
        [Fact]
        public void A_member_the_walk_could_not_read_fails_it_instead_of_reading_as_clean()
        {
            var failure = Assert.Throws<FailException>(() => IndependenceOracle.SharedCollections(new ThrowingProbe(), new AliasProbe()));

            Assert.Contains("root.Hidden: its getter threw InvalidOperationException: not readable in this state", failure.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_graph_deeper_than_the_walk_fails_it_instead_of_reading_as_clean()
        {
            var head = new Link();
            var tail = head;
            for (var i = 0; i < 40; i++)
            {
                tail.Next = new Link();
                tail = tail.Next;
            }

            var failure = Assert.Throws<FailException>(() => IndependenceOracle.SharedCollections(head, new AliasProbe()));

            Assert.Contains("deeper than 32 levels", failure.Message, StringComparison.Ordinal);
            // A cycle is not depth: a node that points back at itself is examined once and passes.
            var loop = new Link();
            loop.Next = loop;
            Assert.Empty(IndependenceOracle.SharedCollections(loop, new AliasProbe()));
        }

        private sealed class AliasProbe
        {
            public List<int> Items { get; set; } = new();
        }

        private sealed class ThrowingProbe
        {
            private readonly string _state = "not readable in this state";

            public List<int> Hidden => throw new InvalidOperationException(_state);
        }

        private sealed class Link
        {
            public Link? Next { get; set; }
        }
    }
}
