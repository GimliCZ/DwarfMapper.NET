// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     I17: an unmapped destination member on ONE mapping method must withhold that method and nothing
    ///     else. <c>DWARF001</c> is an Error and an error suppressed the whole class, so a mapper carrying a
    ///     complete <c>MapGood</c> beside an incomplete <c>MapBad</c> generated NOTHING — <c>MapGood</c> lost
    ///     its implementing part too, exactly as the <c>Map</c> methods were collateral to a projection's
    ///     <c>DWARF028</c> before I14.
    ///     <para>
    ///         <b>The ruling this file pins: DWARF001 is per-METHOD.</b> Completeness is evaluated over one
    ///         (source, target) pair and honours one method-level <c>[MapIgnore]</c> set, and the diagnostic's
    ///         own remedy names the method. Its unit of evaluation and its unit of remedy are both the method,
    ///         so it says nothing about the method beside it — which is precisely the test I14 used the other
    ///         way round to keep <c>DWARF010</c> and <c>DWARF008</c> class-level.
    ///     </para>
    ///     <para>
    ///         <b>The claim is a COUNT, not "it emitted".</b> The build fails either way — DWARF001 is an Error
    ///         and a withheld method still costs its own CS8795 — so the whole difference is diagnostic
    ///         quality, and the only honest way to measure it is to count what the consumer is shown. Before:
    ///         1×DWARF001 + DWARF078 + N×CS8795 + zero bytes. After: 1×DWARF001 + 1×DWARF097 + exactly one
    ///         CS8795, on the method that is actually wrong.
    ///     </para>
    ///     <para>
    ///         <b>The controls are what make it a ruling rather than a blanket.</b> A range that also holds a
    ///         class-level error keeps the whole-class kill; a synthesized pair's incompleteness keeps it too,
    ///         and for a reason rather than an accident of bookkeeping (see
    ///         <see cref="An_incomplete_synthesized_pair_still_kills_the_whole_class" />).
    ///     </para>
    /// </summary>
    public class CompletenessScopedRefusalTests
    {
        /// <summary>Two independent pairs on one mapper; only the second is incomplete.</summary>
        private const string OneIncompleteMethod = """
                                                   using DwarfMapper;
                                                   namespace Demo;

                                                   public sealed class A { public int V { get; set; } }
                                                   public sealed class AD { public int V { get; set; } }
                                                   public sealed class B { public int P { get; set; } }
                                                   public sealed class Y { public int P { get; set; } public int Missing { get; set; } }

                                                   [DwarfMapper]
                                                   public partial class M
                                                   {
                                                       public partial AD MapGood(A a);
                                                       public partial Y MapBad(B b);
                                                   }
                                                   """;

        /// <summary>The same mapper with the incomplete method removed — the byte-comparison baseline.</summary>
        private const string SoloGoodMethod = """
                                              using DwarfMapper;
                                              namespace Demo;

                                              public sealed class A { public int V { get; set; } }
                                              public sealed class AD { public int V { get; set; } }

                                              [DwarfMapper]
                                              public partial class M
                                              {
                                                  public partial AD MapGood(A a);
                                              }
                                              """;

        [Fact]
        public void An_unmapped_member_on_one_method_does_not_suppress_the_others()
        {
            var (diagnostics, generated) = GeneratorTestHarness.Run(OneIncompleteMethod);

            Assert.Contains(diagnostics, d => d.Id == "DWARF001");

            // The signpost moved from the class to the method — and DWARF078's ABSENCE is the assertion,
            // because it is the diagnostic that says "nothing was generated".
            Assert.Contains(diagnostics, d => d.Id == "DWARF097");
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF078");

            // The consumer still gets the method that was never wrong...
            Assert.Contains("public partial global::Demo.AD MapGood(", generated, StringComparison.Ordinal);
            // ...and does NOT get a half-built MapBad, which would return Missing silently unset.
            Assert.DoesNotContain("global::Demo.Y MapBad(", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The cascade, proved by COUNTING. Before the fix both partial methods lost their implementing
        ///     part and DWARF078 announced it; now exactly one does, and it is the one the consumer broke.
        /// </summary>
        [Fact]
        public void Exactly_one_CS8795_follows_and_it_is_the_incomplete_method()
        {
            var (_, errors) = GeneratorTestHarness.EmitAssembly(OneIncompleteMethod);
            var walls = errors.Where(e => e.Id == "CS8795").ToList();

            var only = Assert.Single(walls);
            Assert.Contains("MapBad", only.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        /// <summary>
        ///     The surviving method is not merely PRESENT, it is UNCHANGED: byte-identical to what the same
        ///     mapper emits with the incomplete method deleted. A scoping that quietly perturbed the sibling's
        ///     body — a lost hook, a dropped member — would pass every count above.
        /// </summary>
        [Fact]
        public void The_surviving_method_is_byte_identical_to_a_solo_mapper()
        {
            var withSibling = Body(GeneratorTestHarness.Run(OneIncompleteMethod).GeneratedSource);
            var alone = Body(GeneratorTestHarness.Run(SoloGoodMethod).GeneratedSource);

            Assert.Equal(alone, withSibling);

            static string Body(string generated)
            {
                var start = generated.IndexOf("public partial global::Demo.AD MapGood(", StringComparison.Ordinal);
                Assert.True(start >= 0, "MapGood was not emitted at all:\n" + generated);
                var end = generated.IndexOf("\n}", start, StringComparison.Ordinal);
                return generated.Substring(start, (end < 0 ? generated.Length : end) - start);
            }
        }

        /// <summary>
        ///     The update-into endpoint, which resolves its own members exactly as the create map does, so the
        ///     same ruling reaches it. Named separately because I17's filing demands each family be checked
        ///     rather than assumed from the create map.
        /// </summary>
        [Fact]
        public void An_incomplete_update_into_method_is_withheld_alone()
        {
            const string code = """
                                using DwarfMapper;
                                namespace Demo;

                                public sealed class A { public int V { get; set; } }
                                public sealed class AD { public int V { get; set; } }
                                public sealed class B { public int P { get; set; } }
                                public sealed class Y { public int P { get; set; } public int Missing { get; set; } }

                                [DwarfMapper]
                                public partial class M
                                {
                                    public partial AD MapGood(A a);
                                    public partial void UpdateBad(B b, Y y);
                                }
                                """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(code);
            Assert.Contains(diagnostics, d => d.Id == "DWARF097");
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF078");
            Assert.Contains("MapGood(", generated, StringComparison.Ordinal);

            var (_, errors) = GeneratorTestHarness.EmitAssembly(code);
            var only = Assert.Single(errors.Where(e => e.Id == "CS8795"));
            Assert.Contains("UpdateBad", only.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        /// <summary>
        ///     CONTROL, and the BOUNDARY of the whole ruling, found by probing the fix rather than reasoned
        ///     from it. Withholding a method is only safe while its DECLARATION survives the withholding. A
        ///     partial method is declared by the CONSUMER, so a sibling that maps a nested member through it
        ///     still binds — see <see cref="A_sibling_that_calls_the_withheld_method_still_binds" />. A
        ///     <c>[GenerateMap]</c> pair has no declaration: the generator is the only source of that symbol, so
        ///     withholding it made a sibling's <c>N = Map(o.N)</c> emit
        ///     <b>CS0103, "the name 'Map' does not exist
        ///         in the current context"</b>, in a file the consumer cannot edit — the <c>EmittedInvalidCode</c>
        ///     genre, whose ceiling is exactly zero.
        ///     <para>
        ///         So the pair keeps the whole-class kill, and the CS8795 it costs a sibling stays. That is a
        ///         real wart — the pair has no partial declaration, so every CS8795 a kill produces there is
        ///         collateral — and it is the deliberately-chosen one: loud collateral beats generated code that
        ///         does not compile.
        ///     </para>
        /// </summary>
        [Fact]
        public void An_incomplete_GenerateMap_pair_still_kills_the_whole_class()
        {
            const string code = """
                                using DwarfMapper;
                                namespace Demo;

                                public sealed class Inner { public int P { get; set; } }
                                public sealed class InnerDto { public int P { get; set; } public int Missing { get; set; } }
                                public sealed class Outer { public Inner N { get; set; } = new(); }
                                public sealed class OuterDto { public InnerDto N { get; set; } = new(); }

                                [DwarfMapper]
                                [GenerateMap<Inner, InnerDto>]
                                public partial class M
                                {
                                    public partial OuterDto MapOuter(Outer o);
                                }
                                """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(code);
            Assert.Contains(diagnostics, d => d.Id == "DWARF001");
            Assert.Contains(diagnostics, d => d.Id == "DWARF078");
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF097");
            Assert.Equal("", generated);

            // The assertion that matters: nothing was emitted, so nothing can fail to compile. A regression that
            // withheld the pair would put a call to a method that does not exist into the consumer's build.
            Assert.DoesNotContain(
                GeneratorTestHarness.EmitAssembly(code).Errors,
                e => e.Id == "CS0103");
        }

        /// <summary>
        ///     The reason a withheld PARTIAL method is safe, pinned rather than assumed. <c>MapOuter</c> maps its
        ///     nested member through the declared <c>MapInner</c>, which is withheld — and the call still binds,
        ///     because the consumer's own <c>partial</c> declaration is still in their source. The only error is
        ///     the CS8795 on <c>MapInner</c> itself.
        /// </summary>
        [Fact]
        public void A_sibling_that_calls_the_withheld_method_still_binds()
        {
            const string code = """
                                using DwarfMapper;
                                namespace Demo;

                                public sealed class Inner { public int P { get; set; } }
                                public sealed class InnerDto { public int P { get; set; } public int Missing { get; set; } }
                                public sealed class Outer { public Inner N { get; set; } = new(); }
                                public sealed class OuterDto { public InnerDto N { get; set; } = new(); }

                                [DwarfMapper]
                                public partial class M
                                {
                                    public partial InnerDto MapInner(Inner i);
                                    public partial OuterDto MapOuter(Outer o);
                                }
                                """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(code);
            Assert.Contains(diagnostics, d => d.Id == "DWARF097");
            Assert.Contains("N = MapInner(o.N)", generated, StringComparison.Ordinal);

            var (_, errors) = GeneratorTestHarness.EmitAssembly(code);
            Assert.DoesNotContain(errors, e => e.Id == "CS0103");
            var only = Assert.Single(errors.Where(e => e.Id == "CS8795"));
            Assert.Contains("MapInner", only.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        /// <summary>
        ///     <b>Runtime evidence (B19), and the one shape that can carry it.</b> Everywhere else the build
        ///     fails by construction — DWARF001 is an Error and the withheld method's own CS8795 stands — so
        ///     "it emitted" is the most that can be asserted. An implicitly-private <c>partial void</c>
        ///     declaration is legally allowed to have no implementing part, so it simply disappears: no CS8795,
        ///     no C# error at all, and the assembly LOADS. The surviving sibling can then be CALLED, which is
        ///     what proves a withheld method did not corrupt the emitter's shared state on its way out.
        /// </summary>
        [Fact]
        public void The_surviving_method_still_runs_when_the_withheld_method_costs_no_CS_error()
        {
            const string code = """
                                using DwarfMapper;
                                namespace Demo;

                                public sealed class A { public int V { get; set; } }
                                public sealed class AD { public int V { get; set; } }
                                public sealed class B { public int P { get; set; } }
                                public sealed class Y { public int P { get; set; } public int Missing { get; set; } }

                                [DwarfMapper]
                                public partial class M
                                {
                                    public partial AD MapGood(A a);
                                    partial void UpdateBad(B b, Y y);
                                }
                                """;

            var (diagnostics, _) = GeneratorTestHarness.Run(code);
            Assert.Contains(diagnostics, d => d.Id == "DWARF001");
            Assert.Contains(diagnostics, d => d.Id == "DWARF097");
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF078");

            var (assembly, errors) = GeneratorTestHarness.EmitAssembly(code);
            Assert.True(assembly is not null,
                string.Join(", ", errors.Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture))));

            var mapperType = assembly.GetType("Demo.M")!;
            var mapper = Activator.CreateInstance(mapperType)!;
            var srcType = assembly.GetType("Demo.A")!;
            var source = Activator.CreateInstance(srcType)!;
            srcType.GetProperty("V")!.SetValue(source, 41);

            var mapped = mapperType.GetMethod("MapGood")!.Invoke(mapper, [source])!;
            Assert.Equal(41, mapped.GetType().GetProperty("V")!.GetValue(mapped));

            // The withheld method really is gone, not emitted empty.
            Assert.True(mapperType.GetMethod("UpdateBad",
                BindingFlags.Instance | BindingFlags.NonPublic) is null);
        }

        /// <summary>
        ///     CONTROL. A method whose range holds a class-level error as WELL as the completeness one keeps the
        ///     whole-class kill: DWARF010 describes the SOURCE MODEL and is just as true of every other method
        ///     over that pair, so scoping the range would emit a mapper built on an ambiguity. A fix that scoped
        ///     any range containing a DWARF001 would pass every test above and fail this one.
        /// </summary>
        [Fact]
        public void A_mixed_error_range_still_kills_the_whole_class()
        {
            const string code = """
                                using DwarfMapper;
                                namespace Demo;

                                public sealed class A { public int V { get; set; } }
                                public sealed class AD { public int V { get; set; } }
                                public sealed class B { public int p { get; set; } public int P { get; set; } }
                                public sealed class Y { public int P { get; set; } public int Missing { get; set; } }

                                [DwarfMapper(CaseInsensitive = true)]
                                public partial class M
                                {
                                    public partial AD MapGood(A a);
                                    public partial Y MapBad(B b);
                                }
                                """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(code);
            Assert.Contains(diagnostics, d => d.Id == "DWARF001");
            Assert.Contains(diagnostics, d => d.Id == "DWARF010");
            Assert.Contains(diagnostics, d => d.Id == "DWARF078");
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF097");
            Assert.Equal("", generated);
        }

        /// <summary>
        ///     CONTROL, and a RULING in its own right. A synthesized nested pair's incompleteness keeps the
        ///     whole-class kill, and not merely because the drain runs after the method loop so its DWARF001
        ///     falls outside every range. It is the right answer on the merits, for the reason
        ///     <c>DWARF090</c> already records: a synthesized mapper is SHARED by every route that reaches the
        ///     pair, so its incompleteness is true of each of them, and pinning it on one method would be wrong
        ///     rather than merely hard.
        /// </summary>
        [Fact]
        public void An_incomplete_synthesized_pair_still_kills_the_whole_class()
        {
            const string code = """
                                using DwarfMapper;
                                namespace Demo;

                                public sealed class A { public int V { get; set; } }
                                public sealed class AD { public int V { get; set; } }
                                public sealed class NSrc { public int V { get; set; } }
                                public sealed class NDst { public int V { get; set; } public int Missing { get; set; } }
                                public sealed class B { public NSrc N { get; set; } = new(); }
                                public sealed class Y { public NDst N { get; set; } = new(); }

                                [DwarfMapper]
                                public partial class M
                                {
                                    public partial AD MapGood(A a);
                                    public partial Y MapBad(B b);
                                }
                                """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(code);
            Assert.Contains(diagnostics, d => d.Id == "DWARF001");
            Assert.Contains(diagnostics, d => d.Id == "DWARF078");
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF097");
            Assert.Equal("", generated);
        }

        /// <summary>
        ///     CONTROL, same ruling, the two endpoints it actually bites at. A span map and an async-stream map
        ///     resolve NO members of their own — they map their element pair through an auto-synthesized mapper
        ///     shared by every route to that pair — so an incomplete element pair is not attributable to the
        ///     method that happens to mention it. Both keep the whole-class kill, and this is where that
        ///     consequence is recorded rather than discovered later.
        /// </summary>
        [Theory]
        [InlineData("public partial void Bad(System.ReadOnlySpan<B> src, System.Span<Y> dst);")]
        [InlineData("public partial System.Collections.Generic.IAsyncEnumerable<Y> Bad(" + "System.Collections.Generic.IAsyncEnumerable<B> b);")]
        public void An_incomplete_element_pair_still_kills_the_whole_class(string declaration)
        {
            var code = $$"""
                         using DwarfMapper;
                         namespace Demo;

                         public sealed class A { public int V { get; set; } }
                         public sealed class AD { public int V { get; set; } }
                         public sealed class B { public int P { get; set; } }
                         public sealed class Y { public int P { get; set; } public int Missing { get; set; } }

                         [DwarfMapper]
                         public partial class M
                         {
                             public partial AD MapGood(A a);
                             {{declaration}}
                         }
                         """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(code);
            Assert.Contains(diagnostics, d => d.Id == "DWARF001");
            Assert.Contains(diagnostics, d => d.Id == "DWARF078");
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF097");
            Assert.Equal("", generated);
        }

        /// <summary>
        ///     The DWARF097 twin of I14's late-caught defect. DWARF097 claims "the rest of this mapper WAS
        ///     generated", which is FALSE when some OTHER method on the class carries a class-level error — the
        ///     class dies anyway and DWARF078 says so, so both would be reported and one of them would be lying.
        ///     The scoped signpost stands down and lets the class-wide one speak; the DWARF001 underneath is
        ///     reported either way.
        ///     <para>
        ///         The class-level control above cannot catch this: there the DWARF010 lives in the SAME method's
        ///         range, so no DWARF097 is ever minted. Here the two errors belong to different methods.
        ///     </para>
        /// </summary>
        [Fact]
        public void The_scoped_signpost_stands_down_when_a_class_level_error_kills_everything_anyway()
        {
            const string code = """
                                using DwarfMapper;
                                namespace Demo;

                                public sealed class A { public int v { get; set; } public int V { get; set; } }
                                public sealed class AD { public int V { get; set; } }
                                public sealed class B { public int P { get; set; } }
                                public sealed class Y { public int P { get; set; } public int Missing { get; set; } }

                                [DwarfMapper(CaseInsensitive = true)]
                                public partial class M
                                {
                                    public partial Y MapBad(B b);
                                    public partial AD MapAmbiguous(A a);
                                }
                                """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(code);

            // MapBad's DWARF001 was scopable and MapAmbiguous's DWARF010 is not, so the class dies...
            Assert.Contains(diagnostics, d => d.Id == "DWARF001");
            Assert.Contains(diagnostics, d => d.Id == "DWARF010");
            Assert.Contains(diagnostics, d => d.Id == "DWARF078");
            Assert.Equal("", generated);

            // ...and the per-method signpost, whose whole job is to describe the SCOPE of the damage, is
            // suppressed rather than left claiming a scope that is no longer true.
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF097");
        }

        /// <summary>
        ///     The projection endpoint inherits the same ruling: <c>DWARF001</c> is scopable there too, alone or
        ///     mixed with the <c>DWARF028</c> that endpoint already scoped. Reading completeness as per-method at
        ///     four endpoints and class-level at the fifth would make the SAME error proportional in four places
        ///     and not in the fifth.
        /// </summary>
        [Theory]
        // DWARF001 alone on the projection.
        [InlineData("public sealed class PDst { public int Id { get; set; } public int Missing { get; set; } }")]
        // DWARF001 AND DWARF028 in the same range: a HashSet target is a permanent projection refusal.
        [InlineData("public sealed class PDst { public int Id { get; set; } public int Missing { get; set; } " + "public System.Collections.Generic.HashSet<int> Tags { get; set; } = new(); }")]
        public void An_unmapped_member_on_a_projection_method_is_scoped_to_it(string destination)
        {
            var code = $$"""
                         using System.Collections.Generic;
                         using System.Linq;
                         using DwarfMapper;
                         namespace Demo;

                         public sealed class PSrc { public int Id { get; set; } public List<int> Tags { get; set; } = new(); }
                         {{destination}}

                         [DwarfMapper]
                         public partial class M
                         {
                             public partial PDst Map(PSrc s);
                             public partial IQueryable<PDst> Project(IQueryable<PSrc> q);
                         }
                         """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(code);

            // Map is incomplete too (Missing is unmapped at BOTH endpoints), so both methods are withheld and
            // the class survives with neither. What matters is that nothing claims the CLASS was suppressed.
            Assert.Contains(diagnostics, d => d.Id == "DWARF001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF078");
            Assert.NotEqual("", generated);
        }

        /// <summary>
        ///     The aggregate question I17's filing raises by name: a withheld method must not be referenced by
        ///     the facade, the DI registration or the ambient registry, because the method it would call does
        ///     not exist. It leaves <c>Methods</c> before any of the three is built, and this is where that is
        ///     asserted rather than assumed.
        /// </summary>
        [Fact]
        public void A_withheld_method_is_absent_from_the_facade_DI_and_ambient_registry()
        {
            var (_, generated) = GeneratorTestHarness.RunAll(OneIncompleteMethod);

            Assert.Contains("MapGood", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("MapBad", generated, StringComparison.Ordinal);
            // The withheld pair's TYPES must not appear in a registration either — DwarfProvidesMap, the
            // registry Register call and the ToTarget() extension all key on them.
            Assert.DoesNotContain("typeof(global::Demo.B)", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("global::Demo.Y source", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The private helper a withheld method registered for a nested pair is still emitted, called by
        ///     nothing. MEASURED, not assumed: an unused private METHOD is not a C# warning (unlike an unused
        ///     private field), the file is <c>&lt;auto-generated/&gt;</c> so IDE analyzers skip it, and the
        ///     whole solution still builds 0 warnings / 0 errors. Pruning it would mean reachability analysis
        ///     over the synthesized graph for no measured benefit, and the orphan disappears the moment the
        ///     consumer fixes the method — this test states that trade so it is a decision, not a leak.
        /// </summary>
        [Fact]
        public void A_withheld_methods_nested_helper_is_orphaned_and_that_costs_nothing()
        {
            const string code = """
                                using DwarfMapper;
                                namespace Demo;

                                public sealed class A { public int V { get; set; } }
                                public sealed class AD { public int V { get; set; } }
                                public sealed class NSrc { public int V { get; set; } }
                                public sealed class NDst { public int V { get; set; } }
                                public sealed class B { public NSrc N { get; set; } = new(); }
                                public sealed class Y { public NDst N { get; set; } = new(); public int Missing { get; set; } }

                                [DwarfMapper]
                                public partial class M
                                {
                                    public partial AD MapGood(A a);
                                    public partial Y MapBad(B b);
                                }
                                """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(code);
            Assert.Contains(diagnostics, d => d.Id == "DWARF097");
            Assert.Contains("__DwarfMap_Obj_", generated, StringComparison.Ordinal);
            Assert.Empty(GeneratorTestHarness.GeneratedCodeWarnings(code));
        }

        /// <summary>
        ///     A mapper whose ONLY method is withheld emits an empty but valid partial class, matching what
        ///     DWARF096 already does at the projection endpoint rather than inventing a second answer.
        /// </summary>
        [Fact]
        public void A_mapper_whose_only_method_is_withheld_still_emits_a_valid_class()
        {
            const string code = """
                                using DwarfMapper;
                                namespace Demo;

                                public sealed class B { public int P { get; set; } }
                                public sealed class Y { public int P { get; set; } public int Missing { get; set; } }

                                [DwarfMapper]
                                public partial class M
                                {
                                    public partial Y MapBad(B b);
                                }
                                """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(code);
            Assert.Contains(diagnostics, d => d.Id == "DWARF097");
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF078");
            Assert.Contains("public partial class M", generated, StringComparison.Ordinal);
            Assert.Empty(GeneratorTestHarness.GeneratedCodeWarnings(code));

            var (_, errors) = GeneratorTestHarness.EmitAssembly(code);
            var only = Assert.Single(errors.Where(e => e.Id == "CS8795"));
            Assert.Contains("MapBad", only.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
