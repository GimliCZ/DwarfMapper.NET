// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>A declared mapping method of a mapper that holds user-written code opens with a stack check</b> (round
    ///     35, T20), <b>and no other method does.</b>
    ///     <para>
    ///         The depth guard counts the calls the generator emits. A converter, a hook or a factory the user wrote
    ///         can call a mapping method again, and that loop the generator never sees: measured on a chain 200,000
    ///         levels deep, one process per case, every such route ended in a stack overflow - which cannot be
    ///         caught. <c>RuntimeHelpers.EnsureSufficientExecutionStack()</c> at the entry turns it into
    ///         <c>InsufficientExecutionStackException</c>. It costs about a nanosecond a call (measured: +1.1 ns on a
    ///         4.7 ns class map), so a mapper with no method of its own is not asked to pay it.
    ///     </para>
    ///     <para>What the generated code then does at run time is held by <c>ReentryStackCheckRuntimeTests</c>.</para>
    /// </summary>
    public class ReentryStackCheckTests
    {
        private const string Check = "global::System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();";

        private const string Types = """
                                     using System;
                                     using System.Collections.Generic;
                                     using System.Threading;
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class N { public int V { get; set; } public N? Next { get; set; } }
                                     public class NDto { public int V { get; set; } public NDto? Next { get; set; } }
                                     public class Animal { public string Name { get; set; } = ""; }
                                     public class Dog : Animal { public int Bark { get; set; } }
                                     public class AnimalDto { public string Name { get; set; } = ""; }
                                     public class DogDto : AnimalDto { public int Bark { get; set; } }
                                     """;

        // A method's body opens with the check: the line after its opening brace.
        private static int MethodsOpeningWithTheCheck(string generated)
        {
            return Regex.Count(generated.Replace("\r\n", "\n", StringComparison.Ordinal), @"\)\n\s*\{\n\s*" + Regex.Escape(Check), RegexOptions.CultureInvariant);
        }

        // Every kind of method a caller can enter: create, update-into, span, async stream and dispatch.
        [Fact]
        public void Every_declared_entry_of_a_mapper_with_a_hand_written_method_opens_with_the_check()
        {
            var generated = GeneratorAssert.CompilesClean(Types +
                                                          """

                                                          [DwarfMapper]
                                                          public partial class M
                                                          {
                                                              [MapProperty(nameof(N.Next), nameof(NDto.Next), Use = nameof(Conv))]
                                                              public partial NDto Map(N n);
                                                              [MapProperty(nameof(N.Next), nameof(NDto.Next), Use = nameof(Conv))]
                                                              public partial void Update(N n, NDto d);
                                                              public partial void MapSpan(ReadOnlySpan<N> s, Span<NDto> d);
                                                              public partial IAsyncEnumerable<NDto> MapStream(IAsyncEnumerable<N> s, CancellationToken ct);
                                                              [MapDerivedType<Dog, DogDto>] [MapDerivedType<Animal, AnimalDto>]
                                                              public partial AnimalDto MapAnimal(Animal a);
                                                              private NDto? Conv(N? n) => n is null ? null : Map(n);
                                                          }
                                                          """);

            Assert.Equal(5, MethodsOpeningWithTheCheck(generated));
            // And nothing else carries it: the helpers the generator writes for itself are not entered from outside.
            Assert.Equal(5, Regex.Count(generated, Regex.Escape(Check)));
        }

        // Each kind of user-written code on the mapper, by itself.
        [Theory]
        [InlineData("[AfterMap] private void Finish(N s, NDto d) { }")]
        [InlineData("[BeforeMap] private void Check(N s) { }")]
        [InlineData("private static int Unrelated() => 0;")]
        public void A_hook_or_any_other_method_with_a_body_is_user_written_code(string userMethod)
        {
            var generated = GeneratorAssert.CompilesClean(Types +
                                                          $$"""

                                                            [DwarfMapper]
                                                            public partial class M
                                                            {
                                                                public partial NDto Map(N n);
                                                                {{userMethod}}
                                                            }
                                                            """);

            Assert.Equal(1, MethodsOpeningWithTheCheck(generated));
        }

        // The method may be inherited: a hook or a converter on a base class is bound the same way.
        [Fact]
        public void A_method_inherited_from_a_base_class_counts()
        {
            var generated = GeneratorAssert.CompilesClean(Types +
                                                          """

                                                          public class Helpers { protected static int Unrelated() => 0; }
                                                          [DwarfMapper]
                                                          public partial class M : Helpers
                                                          {
                                                              public partial NDto Map(N n);
                                                          }
                                                          """);

            Assert.Equal(1, MethodsOpeningWithTheCheck(generated));
        }

        // No user-written code, no check: a plain map keeps its cost. Partial declarations, an abstract
        // method and a property are not code that can call the mapper.
        [Theory]
        [InlineData("public partial class M { public partial NDto Map(N n); }")]
        [InlineData("[GenerateMap<N, NDto>] public partial class M { }")]
        [InlineData("public abstract partial class M { public partial NDto Map(N n); protected abstract int Later(); }")]
        [InlineData("public partial class M { public partial NDto Map(N n); public int Setting { get; set; } }")]
        public void A_mapper_with_no_method_body_of_its_own_gets_no_check(string mapper)
        {
            var generated = GeneratorAssert.CompilesClean(Types + "\n[DwarfMapper]\n" + mapper);

            Assert.DoesNotContain("EnsureSufficientExecutionStack", generated, StringComparison.Ordinal);
        }
    }
}
