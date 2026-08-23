// SPDX-License-Identifier: GPL-2.0-only

using Xunit.Sdk;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     B28 — the cheap-count predicate asked whether the source IMPLEMENTS
    ///     <c>ICollection&lt;T&gt;</c>/<c>IReadOnlyCollection&lt;T&gt;</c>, and implementing an interface is not
    ///     the same as exposing a member.
    ///     <para>
    ///         <c>ImmutableArray&lt;T&gt;</c> implements both EXPLICITLY, and so can any user type — reference
    ///         types included, so this was never confined to value types (the class engine's
    ///         <c>sourceIsValueType</c> downgrade masked the struct half and left the reference half live).
    ///         The emitted <c>s.Count</c> then did not bind, in two ways: <c>CS1061</c> in the generated file as
    ///         written, and — with implicit usings on, so <c>System.Linq</c> is in scope — <c>CS1503</c>, where
    ///         <c>s.Count</c> binds to the extension METHOD GROUP, the capacity overload stops matching and
    ///         overload selection quietly moves to <c>List&lt;T&gt;(IEnumerable&lt;T&gt;)</c>. Both measured
    ///         before the fix; the second is the worse one, because a build break that moves overload selection
    ///         is a whisper away from a build that succeeds and does something else.
    ///     </para>
    ///     <para>
    ///         The predicate is now member lookup for classes and structs, and the interface reading for
    ///         interfaces and type parameters — where ordinary lookup really does see the base interface's or
    ///         the constraint's <c>Count</c>. Those cells are pinned here too: a fix that made
    ///         <c>ImmutableArray</c> compile by making <c>IReadOnlyCollection&lt;T&gt;</c> stop pre-sizing would
    ///         pass a test that only checked the broken cells.
    ///     </para>
    /// </summary>
    public class CollectionCountMemberLookupTests
    {
        /// <summary>A reference type whose <c>Count</c> is an EXPLICIT interface implementation.</summary>
        private const string ExplicitCountBag = """
                                                public class ExplicitCountBag : ICollection<Leaf>
                                                {
                                                    private readonly List<Leaf> _items = new();
                                                    int ICollection<Leaf>.Count => _items.Count;
                                                    bool ICollection<Leaf>.IsReadOnly => false;
                                                    void ICollection<Leaf>.Add(Leaf item) => _items.Add(item);
                                                    void ICollection<Leaf>.Clear() => _items.Clear();
                                                    bool ICollection<Leaf>.Contains(Leaf item) => _items.Contains(item);
                                                    void ICollection<Leaf>.CopyTo(Leaf[] a, int i) { }
                                                    bool ICollection<Leaf>.Remove(Leaf item) => _items.Remove(item);
                                                    public IEnumerator<Leaf> GetEnumerator() => _items.GetEnumerator();
                                                    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
                                                }
                                                """;

        /// <summary>
        ///     A derived type that HIDES the inherited <c>int Count</c> with a <c>string</c> one. C# member
        ///     lookup stops at the first type declaring the name, so nothing may pre-size from this — taking the
        ///     hidden base member would emit a capacity argument of the wrong type.
        /// </summary>
        private const string HidingBag = """
                                         public class HidingBag : List<Leaf> { public new string Count => "not an int"; }
                                         """;

        private static string Registry(string member, string extraTypes = "")
        {
            return $$"""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     using System.Collections.Immutable;
                     namespace Demo;
                     public class Leaf { public int V { get; set; } }
                     public class LeafDto { public int V { get; set; } }
                     {{extraTypes}}
                     [MapTo(typeof(Dto))] public class Src { public {{member}} Xs { get; set; } = default!; }
                     public class Dto { public List<LeafDto> Xs { get; set; } = new(); }
                     """;
        }

        private static string ClassEngine(string member, string extraTypes = "")
        {
            return $$"""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     using System.Collections.Immutable;
                     namespace Demo;
                     public class Leaf { public int V { get; set; } }
                     public class LeafDto { public int V { get; set; } }
                     {{extraTypes}}
                     public sealed class S { public {{member}} Xs { get; set; } = default!; }
                     public sealed class D { public List<LeafDto> Xs { get; set; } = new(); }
                     [DwarfMapper]
                     [GenerateMap<S, D>]
                     public partial class M { }
                     """;
        }

        /// <summary>
        ///     Routed through <c>GeneratorAssert</c> rather than calling
        ///     <c>RunAndGetCompilationErrors</c> directly (the adoption ratchet
        ///     <c>FixtureAdoptionScanTests</c> guards): nothing here asserts a particular CS id, only that the
        ///     emission compiles. <paramref name="cell" /> names which matrix cell failed, which the shared
        ///     assertion's source dump does not say for a <c>[Theory]</c>.
        /// </summary>
        private static void AssertCompiles(string source, string cell)
        {
            try
            {
                GeneratorAssert.EmitsCompilableCode(source);
            }
            catch (XunitException e)
            {
                Assert.Fail(cell + " — " + e.Message);
            }
        }

        /// <summary>The capacity argument the registry's synthesized collection helper pre-sizes with.</summary>
        private static string CapacityArg(string source)
        {
            var (_, generated) = GeneratorTestHarness.RunMapToWithSource(source);
            var line = generated.Split('\n').FirstOrDefault(l => l.Contains("__r = new", StringComparison.Ordinal));
            Assert.True(line is not null,
                "the registry emitted no buffered collection helper at all — the probe stopped measuring the " + "thing it names:\n" + generated);
            var open = line.LastIndexOf('(');
            return line[(open + 1)..line.LastIndexOf(')')];
        }

        // ── the defect, both of its failure modes ────────────────────────────────

        [Fact]
        public void An_ImmutableArray_source_compiles_in_the_generated_file_as_written()
        {
            // Was CS1061: 'ImmutableArray<Leaf>' does not contain a definition for 'Count'.
            AssertCompiles(Registry("ImmutableArray<Leaf>"), "registry / ImmutableArray, no usings");
        }

        [Fact]
        public void An_ImmutableArray_source_compiles_with_implicit_usings_in_scope()
        {
            // Was CS1503: Argument 1: cannot convert from 'method group' to 'int' — s.Count bound to
            // Enumerable.Count and overload selection moved off the capacity constructor. A global using in
            // the consumer's own file is exactly what <ImplicitUsings> materializes as GlobalUsings.g.cs, and
            // global usings apply to every syntax tree in the compilation, generated ones included.
            AssertCompiles("global using System.Linq;\n" + Registry("ImmutableArray<Leaf>"),
                "registry / ImmutableArray, implicit usings on");
        }

        [Fact]
        public void An_ImmutableArray_source_pre_sizes_from_Length()
        {
            // Not merely compilable: the count IS cheaply available on ImmutableArray<T>, under the name
            // Length, so the buffer must still be pre-sized. Falling back to no capacity would trade a build
            // break for a silent reallocation loop.
            Assert.Equal("s.Length", CapacityArg(Registry("ImmutableArray<Leaf>")));
        }

        [Fact]
        public void A_reference_type_whose_Count_is_explicit_compiles_and_does_not_pre_size()
        {
            // The half the class engine's value-type downgrade never covered: nothing about this is a struct.
            AssertCompiles(Registry("ExplicitCountBag", ExplicitCountBag), "registry / explicit-Count class");
            Assert.Equal("", CapacityArg(Registry("ExplicitCountBag", ExplicitCountBag)));
        }

        [Fact]
        public void A_hidden_base_Count_is_not_taken_for_the_capacity()
        {
            // public new string Count hides List<Leaf>.Count. Reading past the hiding member would emit
            // new List<LeafDto>("not an int").
            AssertCompiles(Registry("HidingBag", HidingBag), "registry / hidden Count");
            Assert.Equal("", CapacityArg(Registry("HidingBag", HidingBag)));
        }

        // ── the cells that must NOT move ─────────────────────────────────────────

        [Theory]
        // Ordinary member lookup on an interface-typed value sees the base interface's Count, and these are
        // the shapes every pre-sizing source in the repo has today: the fix must leave them byte-identical.
        [InlineData("List<Leaf>")]
        [InlineData("IReadOnlyCollection<Leaf>")]
        [InlineData("ICollection<Leaf>")]
        [InlineData("HashSet<Leaf>")]
        public void A_source_that_really_exposes_Count_still_pre_sizes_from_it(string member)
        {
            AssertCompiles(Registry(member), "registry / " + member);
            Assert.Equal("s.Count", CapacityArg(Registry(member)));
        }

        [Fact]
        public void A_bare_IEnumerable_source_still_emits_no_capacity()
        {
            Assert.Equal("", CapacityArg(Registry("IEnumerable<Leaf>")));
        }

        // ── siblings: the class engine and the dictionary engine ask the same question ──

        [Theory]
        [InlineData("ExplicitCountBag")]
        [InlineData("HidingBag")]
        public void The_class_engine_reads_the_same_predicate_and_compiles_too(string member)
        {
            // CollectionConverter.CapacityArg / EmitArray emit src.Count from the very same helper. The struct
            // half was masked by Shape's sourceIsValueType downgrade (Nullable<T> exposes no count); the
            // reference half was live and produced CS1061, measured before the fix.
            var extra = string.Equals(member, "ExplicitCountBag", StringComparison.Ordinal)
                ? ExplicitCountBag
                : HidingBag;
            AssertCompiles(ClassEngine(member, extra), "class engine / " + member);
        }

        [Fact]
        public void The_dictionary_engine_reads_the_same_predicate()
        {
            // new Dictionary(src.Count) is the identical question one file over: DictionaryConverter carried
            // its own copy of the interface test and now shares the predicate.
            const string source = """
                                  using DwarfMapper;
                                  using System.Collections.Generic;
                                  namespace Demo;
                                  public class ExplicitCountMap : IEnumerable<KeyValuePair<string, int>>,
                                                                  IReadOnlyCollection<KeyValuePair<string, int>>
                                  {
                                      private readonly Dictionary<string, int> _items = new();
                                      int IReadOnlyCollection<KeyValuePair<string, int>>.Count => _items.Count;
                                      public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => _items.GetEnumerator();
                                      System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
                                  }
                                  public sealed class S { public ExplicitCountMap Xs { get; set; } = default!; }
                                  public sealed class D { public Dictionary<string, int> Xs { get; set; } = new(); }
                                  [DwarfMapper]
                                  [GenerateMap<S, D>]
                                  public partial class M { }
                                  """;
            AssertCompiles(source, "dictionary engine / explicit-Count map");
        }
    }
}
