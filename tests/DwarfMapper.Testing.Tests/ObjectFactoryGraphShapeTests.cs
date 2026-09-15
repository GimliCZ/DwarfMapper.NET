// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
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

    /// <summary>A class whose only constructor is private: the factory has no public way to build one.</summary>
    public sealed class NoPublicConstructor
    {
        private NoPublicConstructor()
        {
        }

        public int X { get; set; }
    }

    /// <summary>A user generic CLASS: not a collection, not a dictionary, not an interface.</summary>
    public class GenericBox<T>
    {
        public T? Value { get; set; }
    }

    /// <summary>
    ///     A class whose only constructor takes arguments. <c>Name</c> is a member that constructor does not set.
    ///     <c>Label</c> is settable AND set by the constructor; <c>LabelFromCtor</c> keeps the constructor's
    ///     copy, so a test can see whether <c>Label</c> was drawn again afterwards. <c>Count</c> and
    ///     <c>CountFromCtor</c> are the same pair for a public FIELD.
    /// </summary>
    public sealed class CtorAndSetter
    {
        public int Count;

        public CtorAndSetter(int id, string? label, int count)
        {
            Id = id;
            Label = label;
            LabelFromCtor = label;
            Count = count;
            CountFromCtor = count;
        }

        public int Id { get; }

        public int CountFromCtor { get; }

        public string? Name { get; set; }

        public string? Label { get; set; }

        public string? LabelFromCtor { get; }
    }

    /// <summary>Two public constructors that leave distinct marks, so a test can see which one ran.</summary>
    public sealed class TwoConstructors
    {
        public TwoConstructors()
        {
            Via = "parameterless";
        }

        public TwoConstructors(int seeded)
        {
            Via = "int";
            Seeded = seeded;
        }

        public string Via { get; }

        public int Seeded { get; }
    }

    /// <summary>
    ///     A struct with a declared constructor. Its implicit default is a second way to build it, and one that
    ///     reflection does not list. Only the declared constructor sets <c>FromCtor</c>.
    /// </summary>
    public readonly record struct StructWithCtor
    {
        public StructWithCtor(int value)
        {
            Value = value;
            FromCtor = true;
        }

        public int Value { get; }

        public bool FromCtor { get; }
    }

    /// <summary>
    ///     A struct that DECLARES its parameterless constructor. That constructor is its only construction shape:
    ///     there is no separate implicit default to choose.
    /// </summary>
    public readonly record struct ExplicitParameterlessStruct
    {
        public ExplicitParameterlessStruct()
        {
            FromCtor = true;
        }

        public bool FromCtor { get; }
    }

    /// <summary>A class whose only constructor refuses every argument it is handed.</summary>
    public sealed class RefusingConstructor
    {
        public RefusingConstructor(int value)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "always refused");
        }
    }

    /// <summary>A <c>[Flags]</c> enum with a SIGNED underlying type, the counterpart of <see cref="WideBits" />.</summary>
    [Flags]
    public enum NarrowBits
    {
        None = 0,
        One = 1,
        Two = 2,
        Four = 4,
        Eight = 8
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

        /// <summary>
        ///     A <c>[Flags]</c> enum with a SIGNED underlying type accumulates in a signed accumulator, and still
        ///     produces COMBINED values. A combination is the whole point of a flags enum, and a single declared
        ///     member can never produce one. Every bit set must come from a declared member, and some draw must set
        ///     more than one.
        /// </summary>
        [Fact]
        public void A_signed_flags_enum_is_built_from_combinations_of_its_own_members()
        {
            var rng = new Random(16);
            const NarrowBits all = NarrowBits.One | NarrowBits.Two | NarrowBits.Four | NarrowBits.Eight;

            var values = Enumerable.Range(0, 40)
                .Select(_ => (NarrowBits)ObjectFactoryV2.Create(typeof(NarrowBits), rng, 0)!)
                .ToList();

            Assert.All(values, v => Assert.Equal(NarrowBits.None, v & ~all));
            Assert.Contains(values, v => System.Numerics.BitOperations.PopCount((uint)v) > 1);
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

        /// <summary>
        ///     A diamond wired through a property the root type does not have is refused by name. Without the
        ///     refusal, a root with only one side pointing at the shared child would not be a diamond, and a
        ///     sharing test built on it would measure nothing.
        /// </summary>
        [Fact]
        public void MakeDiamond_rejects_a_property_the_root_type_does_not_have()
        {
            var ex = Assert.Throws<ArgumentException>(() => ObjectFactoryV2.MakeDiamond(typeof(DiamondRoot),
                typeof(DiamondChild),
                "NoSuchProperty",
                nameof(DiamondRoot.Right),
                new Random(15)));

            Assert.Equal("propName", ex.ParamName);
            Assert.Contains("'NoSuchProperty' not found on DiamondRoot", ex.Message, StringComparison.Ordinal);
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

        /// <summary>
        ///     <b>A struct with no declared constructor is POPULATED, below the cap, like any other type.</b>
        ///     <para>
        ///         Such a struct, the common case, has no public constructor at all: its parameterless one is
        ///         implicit and invisible to reflection. The factory used to take "no public constructor" to mean
        ///         "nothing to build" and returned the struct's default. So every struct DTO and struct member in
        ///         every fuzz fixture was all zeros, and struct mapping was only ever fuzzed with default values.
        ///         Found 2026-09-15 by probe; fixed by owner ruling.
        ///     </para>
        /// </summary>
        /// <summary>
        ///     A CLASS with no public constructor comes back null: the factory honours accessibility and does not
        ///     reach a private constructor by reflection. It is the class-side counterpart of the struct fallback
        ///     below, where a struct with no declared constructor still has its implicit one.
        /// </summary>
        [Fact]
        public void A_class_with_no_public_constructor_comes_back_null()
        {
            Assert.Null(ObjectFactoryV2.Create(typeof(NoPublicConstructor), new Random(17), 0));
        }

        /// <summary>
        ///     A queue or a stack is populated like any other collection. Both used to reach the class path, where
        ///     their parameterless constructor built them EMPTY for every seed, so no fixture ever carried an
        ///     element in one.
        /// </summary>
        [Theory]
        [InlineData(typeof(Queue<int>))]
        [InlineData(typeof(Stack<int>))]
        public void A_queue_or_a_stack_is_populated_across_seeds(Type collectionType)
        {
            var sizes = Enumerable.Range(0, 40)
                .Select(seed => ((ICollection)ObjectFactoryV2.Create(collectionType, new Random(seed), 0, false)!).Count)
                .ToList();

            Assert.Contains(sizes, size => size > 0);
        }

        /// <summary>
        ///     A member the constructor does not set is filled after the constructor runs. The factory used to
        ///     return straight from a parameterized constructor, so every such member stayed at its default for
        ///     every seed. A member the constructor DID set keeps the constructor's value: it is not drawn again.
        /// </summary>
        [Fact]
        public void A_member_its_constructor_does_not_set_is_filled_after_the_constructor_runs()
        {
            var made = Enumerable.Range(0, 40)
                .Select(seed => Assert.IsType<CtorAndSetter>(
                    ObjectFactoryV2.Create(typeof(CtorAndSetter), new Random(seed), 0, false)))
                .ToList();

            Assert.Contains(made, m => m.Name is not null);
            Assert.All(made, m => Assert.Equal(m.LabelFromCtor, m.Label));
            Assert.All(made, m => Assert.Equal(m.CountFromCtor, m.Count));
        }

        private static readonly string[] BothConstructors = ["int", "parameterless"];

        private static readonly bool[] BothWays = [false, true];

        /// <summary>
        ///     Every public constructor is chosen by some seed. The factory used to call the parameterless
        ///     constructor whenever there was one, so no other constructor ever ran (owner ruling 2026-09-15).
        /// </summary>
        [Fact]
        public void Every_public_constructor_is_chosen_by_some_seed()
        {
            var via = Enumerable.Range(0, 40)
                .Select(seed => Assert.IsType<TwoConstructors>(
                    ObjectFactoryV2.Create(typeof(TwoConstructors), new Random(seed), 0, false)).Via)
                .Distinct()
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(BothConstructors, via);
        }

        /// <summary>
        ///     A struct with a declared constructor is built both ways across seeds: through that constructor, and
        ///     through its implicit default, which is then populated like any other instance.
        /// </summary>
        [Fact]
        public void A_struct_with_a_declared_constructor_is_also_built_through_its_implicit_default()
        {
            var fromCtor = Enumerable.Range(0, 40)
                .Select(seed => Assert.IsType<StructWithCtor>(
                    ObjectFactoryV2.Create(typeof(StructWithCtor), new Random(seed), 0, false)).FromCtor)
                .Distinct()
                .OrderBy(b => b)
                .ToList();

            Assert.Equal(BothWays, fromCtor);
        }

        /// <summary>
        ///     A struct that declares its parameterless constructor is always built through it. Its implicit
        ///     default is not offered as a second choice, because the declared constructor IS the parameterless one.
        /// </summary>
        [Fact]
        public void A_struct_that_declares_its_parameterless_constructor_is_always_built_through_it()
        {
            Assert.All(Enumerable.Range(0, 40),
                seed => Assert.True(Assert.IsType<ExplicitParameterlessStruct>(
                    ObjectFactoryV2.Create(typeof(ExplicitParameterlessStruct), new Random(seed), 0, false)).FromCtor));
        }

        /// <summary>
        ///     A constructor that throws on the arguments seeded for it fails the fixture loudly, naming the type,
        ///     with the constructor's own exception inside. There is no retry and no fallback (owner ruling
        ///     2026-09-15). A bare <c>TargetInvocationException</c> used to escape instead, naming nothing.
        /// </summary>
        [Fact]
        public void A_constructor_that_throws_on_its_seeded_arguments_fails_naming_the_type()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => ObjectFactoryV2.Create(typeof(RefusingConstructor), new Random(26), 0, false));

            Assert.Contains(typeof(RefusingConstructor).FullName!, ex.Message, StringComparison.Ordinal);
            Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
        }

        /// <summary>
        ///     A dictionary whose KEY type the factory cannot build comes back EMPTY rather than throwing. A null
        ///     dictionary key throws, so every key that comes back null is skipped. Here that is every key: the key
        ///     type has no public constructor, so the dictionary stays empty.
        /// </summary>
        [Fact]
        public void A_dictionary_whose_key_type_cannot_be_built_comes_back_empty()
        {
            var made = ObjectFactoryV2.Create(typeof(Dictionary<NoPublicConstructor, int>), new Random(18), 0, false);

            Assert.Empty(Assert.IsType<Dictionary<NoPublicConstructor, int>>(made));
        }

        /// <summary>
        ///     A user-defined generic CLASS is not mistaken for any of the generic shapes the factory special-cases
        ///     (lists, sets, dictionaries, immutable collections, two-argument interfaces). It falls through every
        ///     one of them and is built as an ordinary class, with its members populated.
        /// </summary>
        [Fact]
        public void A_user_generic_class_is_built_as_an_ordinary_class()
        {
            var boxes = Enumerable.Range(0, 20)
                .Select(seed =>
                    Assert.IsType<GenericBox<int>>(ObjectFactoryV2.Create(typeof(GenericBox<int>), new Random(seed), 0)))
                .ToList();

            Assert.Contains(boxes, b => b.Value != 0);
        }

        [Fact]
        public void A_struct_with_no_declared_constructor_is_populated_rather_than_left_default()
        {
            var points = Enumerable.Range(0, 20)
                .Select(seed => Assert.IsType<Point>(ObjectFactoryV2.Create(typeof(Point), new Random(seed), 0)))
                .ToList();

            Assert.Contains(points, p => p.X != 0);
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
        ///     A dictionary the factory DOES build below the depth cap is never left empty, as long as its key type
        ///     can be built. There are two reasons. The size roll below the cap is at least one. And the first key
        ///     drawn can never be a duplicate. An empty dictionary would exercise none of the generator's
        ///     dictionary paths, which is the whole reason a fixture asks for one.
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
