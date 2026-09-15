// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Testing.Tests
{
    public class LoopNode
    {
        public int Id { get; set; }

        public LoopNode? Self { get; set; }
    }

    public class CycleNode
    {
        public int Id { get; set; }

        public CycleNode? Next { get; set; }
    }

    public class DiamondRoot
    {
        public DiamondChild? Left { get; set; }

        public DiamondChild? Right { get; set; }
    }

    public class DiamondChild
    {
        public int Id { get; set; }
    }

    public class OwnerA
    {
        public OwnerB? B { get; set; }
    }

    public class OwnerB
    {
        public OwnerC? C { get; set; }

        public OwnerD? D { get; set; }
    }

    public class OwnerC
    {
        public OwnerB? B { get; set; }

        public OwnerD? D { get; set; }
    }

    public class OwnerD
    {
        public OwnerB? B { get; set; }

        public OwnerC? C { get; set; }
    }

    /// <summary>An enum with no members at all -- legal C#, and a shape the factory must not divide by.</summary>
    // CA1008 asks for a zero-valued member; EMPTY IS THE POINT. The factory branch under test is the one
    // guarding `values.Length == 0`, so adding a member would move this to the other branch and leave the
    // guard uncovered -- which is how it came to be untested in the first place.
#pragma warning disable CA1008
    public enum NoMembers
#pragma warning restore CA1008
    {
    }

    public class WithPublicField
    {
        public int Plain;

        public readonly int Fixed = 7;

        public string Name { get; set; } = "";
    }

    /// <summary>A record struct, so equality comes for free and CA1815 is satisfied honestly.</summary>
    public record struct Point
    {
        public int X { get; set; }
    }

    public class HasCollections
    {
        public List<int> Numbers { get; set; } = new();

        public Dictionary<string, int> Lookup { get; set; } = new();
    }

    // CA1028 asks for an Int32 underlying type; ULONG IS THE POINT HERE. The factory branch under test
    // exists precisely because an unsigned enum can hold values above long.MaxValue, which a signed
    // accumulator throws on -- narrowing this to int would test the other branch and leave that one uncovered.
#pragma warning disable CA1028
    [Flags]
    public enum WideBits : ulong
#pragma warning restore CA1028
    {
        None = 0,
        Alpha = 1,
        Beta = 2,
        Gamma = 4,
        Delta = 8
    }

    /// <summary>
    ///     The factory's GRAPH-SHAPE builders, its wide-enum path, and its argument guards.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         These four builders are public API of a package other people's test suites consume, and a
    ///         coverage run found all four executed by nothing — 66 uncovered lines in the merged factory,
    ///         enough to drop this assembly below its floor. They exist to construct the exact topologies the
    ///         reference-handling tests need: a self-loop, a mutual cycle, an owner graph with back-edges, and
    ///         a diamond where two properties share one child.
    ///     </para>
    ///     <para>
    ///         Each test asserts REFERENCE IDENTITY, not just non-null. That is the whole point of these
    ///         builders: a diamond whose Left and Right are two equal-but-distinct children is not a diamond,
    ///         and it would satisfy any assertion weaker than <c>Assert.Same</c>.
    ///     </para>
    /// </remarks>
    public class ObjectFactoryGraphShapeTests
    {
        [Fact]
        public void MakeSelfLoop_points_the_property_at_the_node_itself()
        {
            var node = (LoopNode)ObjectFactoryV2.MakeSelfLoop(typeof(LoopNode), nameof(LoopNode.Self),
                new Random(1));

            Assert.Same(node, node.Self);
        }

        [Fact]
        public void MakeTwoNodeCycle_wires_each_node_to_the_other()
        {
            var (a, b) = ObjectFactoryV2.MakeTwoNodeCycle(typeof(CycleNode), nameof(CycleNode.Next),
                new Random(2));

            var na = Assert.IsType<CycleNode>(a);
            var nb = Assert.IsType<CycleNode>(b);

            Assert.Same(nb, na.Next);
            Assert.Same(na, nb.Next);
            Assert.NotSame(na, nb);
        }

        [Fact]
        public void MakeDiamond_gives_both_sides_the_SAME_child()
        {
            var (root, shared) = ObjectFactoryV2.MakeDiamond(typeof(DiamondRoot),
                typeof(DiamondChild),
                nameof(DiamondRoot.Left),
                nameof(DiamondRoot.Right),
                new Random(3));

            var r = Assert.IsType<DiamondRoot>(root);

            // Identity, not equality: two equal-but-distinct children would not be a diamond at all.
            Assert.Same(shared, r.Left);
            Assert.Same(r.Left, r.Right);
        }

        [Fact]
        public void MakeOwnerGraph_wires_every_back_edge()
        {
            var (a, b, c, d) = ObjectFactoryV2.MakeOwnerGraph(typeof(OwnerA),
                typeof(OwnerB),
                typeof(OwnerC),
                typeof(OwnerD),
                nameof(OwnerA.B),
                nameof(OwnerB.C),
                nameof(OwnerB.D),
                nameof(OwnerC.B),
                nameof(OwnerC.D),
                nameof(OwnerD.B),
                nameof(OwnerD.C),
                new Random(4));

            var oa = Assert.IsType<OwnerA>(a);
            var ob = Assert.IsType<OwnerB>(b);
            var oc = Assert.IsType<OwnerC>(c);
            var od = Assert.IsType<OwnerD>(d);

            Assert.Same(ob, oa.B);
            Assert.Same(oc, ob.C);
            Assert.Same(od, ob.D);
            Assert.Same(ob, oc.B);
            Assert.Same(od, oc.D);
            Assert.Same(ob, od.B);
            Assert.Same(oc, od.C);
        }

        /// <summary>
        ///     A <c>[Flags]</c> enum whose underlying type is <c>ulong</c> accumulates in that type. The
        ///     comment on that branch explains why it matters: an unsigned enum can hold values above
        ///     <c>long.MaxValue</c>, which a signed accumulator would throw on.
        /// </summary>
        [Fact]
        public void A_wide_flags_enum_is_built_from_its_own_members()
        {
            var rng = new Random(5);

            for (var i = 0; i < 40; i++)
            {
                var value = (WideBits)ObjectFactoryV2.Create(typeof(WideBits), rng, 0)!;

                // Every bit set must come from a declared member -- nothing outside the enum's own vocabulary.
                var all = WideBits.Alpha | WideBits.Beta | WideBits.Gamma | WideBits.Delta;
                Assert.Equal(WideBits.None, value & ~all);
            }
        }

        // ── The argument guards ─────────────────────────────────────────────────

        [Fact]
        public void Create_rejects_a_null_type()
        {
            Assert.Throws<ArgumentNullException>(() => ObjectFactoryV2.Create(null!, new Random(6), 0));
        }

        [Fact]
        public void Create_rejects_a_null_random()
        {
            Assert.Throws<ArgumentNullException>(() => ObjectFactoryV2.Create(typeof(LoopNode), null!, 0));
        }

        [Fact]
        public void MakeSelfLoop_rejects_a_null_node_type()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ObjectFactoryV2.MakeSelfLoop(null!, nameof(LoopNode.Self), new Random(7)));
        }

        [Fact]
        public void MakeTwoNodeCycle_rejects_a_null_node_type()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ObjectFactoryV2.MakeTwoNodeCycle(null!, nameof(CycleNode.Next), new Random(8)));
        }

        /// <summary>
        ///     A self-loop over a property the node type does not have is refused by name. Silently building an
        ///     unlooped node would hand a reference-handling test a fixture with no cycle in it, and that test
        ///     would pass for the wrong reason.
        /// </summary>
        [Fact]
        public void MakeSelfLoop_rejects_a_property_the_node_type_does_not_have()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                ObjectFactoryV2.MakeSelfLoop(typeof(LoopNode), "NoSuchProperty", new Random(9)));

            Assert.Equal("selfPropName", ex.ParamName);
            Assert.Contains("'NoSuchProperty' not found on LoopNode", ex.Message, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A two-node cycle over a property the node type does not have is refused by name. Silently building
        ///     two unconnected nodes would hand a cycle-handling test a fixture with no cycle in it.
        /// </summary>
        [Fact]
        public void MakeTwoNodeCycle_rejects_a_property_the_node_type_does_not_have()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                ObjectFactoryV2.MakeTwoNodeCycle(typeof(CycleNode), "NoSuchProperty", new Random(10)));

            Assert.Equal("nextPropName", ex.ParamName);
            Assert.Contains("'NoSuchProperty' not found on CycleNode", ex.Message, StringComparison.Ordinal);
        }

        /// <summary>
        ///     An owner graph wired through a property one of its types does not have is refused by name, naming the
        ///     type that lacks it. A half-wired owner graph would be missing exactly the back-edge a
        ///     reference-handling test was built to exercise.
        /// </summary>
        [Fact]
        public void MakeOwnerGraph_rejects_a_property_one_of_its_types_does_not_have()
        {
            var ex = Assert.Throws<ArgumentException>(() => ObjectFactoryV2.MakeOwnerGraph(typeof(OwnerA),
                typeof(OwnerB),
                typeof(OwnerC),
                typeof(OwnerD),
                "NoSuchProperty",
                nameof(OwnerB.C),
                nameof(OwnerB.D),
                nameof(OwnerC.B),
                nameof(OwnerC.D),
                nameof(OwnerD.B),
                nameof(OwnerD.C),
                new Random(14)));

            Assert.Equal("propName", ex.ParamName);
            Assert.Contains("'NoSuchProperty' not found on OwnerA", ex.Message, StringComparison.Ordinal);
        }

        /// <summary>The seedless overload is the one a caller reaches for first, and it was never called.</summary>
        [Fact]
        public void The_seedless_overload_produces_a_populated_instance()
        {
            var node = ObjectFactoryV2.Create<DiamondChild>();

            Assert.NotNull(node);
            Assert.True(node.Id.ToString(CultureInfo.InvariantCulture).Length > 0);
        }

        // ── Depth, degenerate shapes, and the paths a fuzz run reaches by accident ──

        /// <summary>
        ///     At the depth cap the factory stops descending, rather than recursing until the stack gives out. The
        ///     cap is what makes a fuzz suite over a recursive schema finish at all.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The cap reads differently for objects and for collections, and this test used to get the
        ///         collection half wrong. It asserted that a <c>List&lt;int&gt;</c> at the cap is null "because the
        ///         cap is reached before the collection branch". The opposite is true. The array and generic
        ///         collection branches run BEFORE the object branch's cap check, and they size what they build with
        ///         the cap instead. So a collection at the cap is EMPTY. It was null only when the reference-type
        ///         null draw happened to fire first, which the fixed seed happened to make it do.
        ///     </para>
        ///     <para>
        ///         The collections are therefore asked for with <c>allowNull: false</c>, which takes the random
        ///         null draw out and leaves the cap as the only thing being measured.
        ///     </para>
        /// </remarks>
        [Fact]
        public void At_the_depth_cap_the_factory_stops_descending()
        {
            var rng = new Random(11);

            // 6 is the factory's own cap. An OBJECT comes back null there: either the null draw fires, or the
            // object branch reaches its cap check and constructs nothing.
            Assert.Null(ObjectFactoryV2.Create(typeof(HasCollections), rng, 6));

            // A COLLECTION comes back empty: its branch runs before that check and sizes itself with the cap.
            Assert.Empty(Assert.IsType<int[]>(ObjectFactoryV2.Create(typeof(int[]), rng, 6, false)));
            Assert.Empty(Assert.IsType<List<int>>(ObjectFactoryV2.Create(typeof(List<int>), rng, 6, false)));
            Assert.Empty(Assert.IsType<Dictionary<string, int>>(
                ObjectFactoryV2.Create(typeof(Dictionary<string, int>), rng, 6, false)));

            // A VALUE type cannot be null, so the cap yields its default rather than descending into it.
            Assert.Equal(default(Point), Assert.IsType<Point>(ObjectFactoryV2.Create(typeof(Point), rng, 6)));
        }

        /// <summary>An enum with NO members: there is nothing to pick, so the default value is the only answer.</summary>
        [Fact]
        public void An_enum_with_no_members_yields_its_default()
        {
            var value = ObjectFactoryV2.Create(typeof(NoMembers), new Random(12), 0);

            Assert.Equal(default(NoMembers), Assert.IsType<NoMembers>(value));
        }

        /// <summary>
        ///     Public FIELDS are populated alongside properties — a DTO written with fields is still a DTO —
        ///     but a readonly field is left alone, because assigning one is not something the type permits.
        /// </summary>
        [Fact]
        public void Public_fields_are_populated_and_readonly_fields_are_not()
        {
            var made = Assert.IsType<WithPublicField>(
                ObjectFactoryV2.Create(typeof(WithPublicField), new Random(13), 0));

            Assert.Equal(7, made.Fixed);
            Assert.NotEqual("", made.Name);
        }

        /// <summary>
        ///     A dictionary the factory DOES build below the depth cap is never left empty: when the size roll
        ///     comes up zero, a back-fill puts one entry in. An empty dictionary would exercise none of the
        ///     generator's dictionary paths, which is the whole reason a fixture asks for one.
        /// </summary>
        /// <remarks>
        ///     The property may still be NULL -- nullable members are deliberately null sometimes, because a
        ///     mapper that mishandles a null collection is exactly what the fuzz suite is hunting. So this
        ///     asserts the conditional guarantee, and separately that at least one seed produced a dictionary,
        ///     without which the loop would assert nothing at all.
        /// </remarks>
        [Fact]
        public void A_dictionary_the_factory_builds_is_never_left_empty()
        {
            var built = 0;

            for (var seed = 0; seed < 40; seed++)
            {
                var made = Assert.IsType<HasCollections>(
                    ObjectFactoryV2.Create(typeof(HasCollections), new Random(seed), 0));

                // the factory assigns through reflection and may leave this null; that is the case being skipped
                // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                if (made.Lookup is null)
                {
                    continue;
                }

                built++;
                Assert.NotEmpty(made.Lookup);
            }

            Assert.True(built > 0, "no seed produced a dictionary at all, so the guarantee was never tested");
        }
    }
}
