// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;
using Microsoft.CodeAnalysis;

// Adversary suite: hostile, degenerate and malformed inputs aimed at making DwarfGenerator THROW.
//
// The contract under test is not "produces good output" - it is "never crashes". Roslyn parks a
// generator exception on GeneratorRunResult.Exception and downgrades it to a CS8785 warning, so a
// crashing generator looks exactly like a refusing one: no sources, build still reports 0 errors.
// A consumer then loses EVERY generated map with no error to chase. That is not hypothetical - it
// shipped, as an ArgumentOutOfRangeException out of LocationInfo.From reported from a consuming solution.
//
// Refusing with a diagnostic is a PASS here. Emitting nothing is a PASS. Throwing is the only failure.
namespace DwarfMapper.Generator.Tests.Fuzzing
{
    public class GeneratorResilienceAdversarialTests
    {
        /// <summary>
        ///     Runs the generator and fails only if it threw. <see cref="GeneratorRunner.Run" /> asserts
        ///     <c>GeneratorRunResult.Exception is null</c>, so any crash surfaces here as a thrown
        ///     InvalidOperationException carrying the original stack.
        /// </summary>
        private static void NeverThrows(string source)
        {
            var ex = Record.Exception(() => GeneratorRunner.Run(new DwarfGenerator(), source));
            Assert.Null(ex);
        }

        // ─── Non-vacuity control ──────────────────────────────────────────────────

        /// <summary>A generator that always throws, used to prove the detector below actually detects.</summary>
        private sealed class ThrowingGenerator : IIncrementalGenerator
        {
            public void Initialize(IncrementalGeneratorInitializationContext context)
            {
                context.RegisterSourceOutput(
                    context.CompilationProvider,
                    static (_, _) => throw new InvalidTimeZoneException("deliberate crash"));
            }
        }

        [Fact]
        public void The_harness_actually_fails_when_a_generator_throws()
        {
            // Without this control the 24 attacks below would pass even if GeneratorRunner silently
            // swallowed crashes - which is precisely the failure mode they exist to catch. Roslyn hides
            // the exception on GeneratorRunResult.Exception, so "no exception escaped RunGenerators" is
            // NOT evidence the generator survived.
            var ex = Record.Exception(() => GeneratorRunner.Run(new ThrowingGenerator(), "class C {}"));

            Assert.NotNull(ex);
            Assert.Contains("THREW", ex.Message, StringComparison.Ordinal);
            Assert.Contains("InvalidTimeZoneException", ex.Message, StringComparison.Ordinal);
        }

        // ─── Malformed / unparseable input ────────────────────────────────────────
        // Roslyn runs generators on compilations that do not parse. Every syntax walk must cope with
        // missing nodes rather than assuming a well-formed tree.

        [Fact]
        public void Truncated_class_body_does_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class A { public int X { get; set; } }
                        public class B { public int X { get; set; } }
                        [DwarfMapper]
                        public partial class M
                        {
                            public partial B To(A a);
                        """);
        }

        [Fact]
        public void Attribute_with_unclosed_generic_does_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class A { public int X { get; set; } }
                        [GenerateMap<A,
                        public partial class M { }
                        """);
        }

        [Fact]
        public void Garbage_tokens_around_the_attribute_do_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        ??? [DwarfMapper] ###
                        public partial class M { public partial int To(int a); }
                        """);
        }

        // ─── Unresolved / error symbols ───────────────────────────────────────────
        // A compilation with errors still runs generators, so ITypeSymbol can be IErrorTypeSymbol.
        // Anything that assumes a resolvable type, an existing member, or a non-null SourceTree dies here.

        [Fact]
        public void Map_pair_naming_types_that_do_not_exist_does_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        [DwarfMapper]
                        [GenerateMap<NoSuchSource, NoSuchTarget>]
                        public partial class M { }
                        """);
        }

        [Fact]
        public void Method_returning_an_unresolved_type_does_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class A { public int X { get; set; } }
                        [DwarfMapper]
                        public partial class M { public partial Missing To(A a); }
                        """);
        }

        [Fact]
        public void MapProperty_naming_a_member_that_does_not_exist_does_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class A { public int X { get; set; } }
                        public class B { public int Y { get; set; } }
                        [DwarfMapper]
                        [GenerateMap<A, B>]
                        [MapProperty<A, B>("NopeSource", "NopeTarget")]
                        public partial class M { }
                        """);
        }

        [Fact]
        public void Missing_using_so_the_attribute_itself_is_an_error_type_does_not_crash()
        {
            NeverThrows("""
                        namespace Demo;
                        public class A { public int X { get; set; } }
                        public class B { public int X { get; set; } }
                        [DwarfMapper]
                        public partial class M { public partial B To(A a); }
                        """);
        }

        // ─── Degenerate shapes ────────────────────────────────────────────────────

        [Fact]
        public void Self_referential_type_does_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class Node { public Node? Next { get; set; } public int V { get; set; } }
                        public class NodeDto { public NodeDto? Next { get; set; } public int V { get; set; } }
                        [DwarfMapper]
                        public partial class M { public partial NodeDto To(Node n); }
                        """);
        }

        [Fact]
        public void Mutually_recursive_pair_does_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class A { public B? B { get; set; } }
                        public class B { public A? A { get; set; } }
                        public class ADto { public BDto? B { get; set; } }
                        public class BDto { public ADto? A { get; set; } }
                        [DwarfMapper]
                        public partial class M
                        {
                            public partial ADto To(A a);
                            public partial BDto To(B b);
                        }
                        """);
        }

        [Fact]
        public void Mapping_a_type_to_itself_does_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class A { public int X { get; set; } }
                        [DwarfMapper]
                        public partial class M { public partial A To(A a); }
                        """);
        }

        [Fact]
        public void Type_with_no_members_at_all_does_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class A { }
                        public class B { }
                        [DwarfMapper]
                        public partial class M { public partial B To(A a); }
                        """);
        }

        [Fact]
        public void Open_generic_mapper_and_generic_pair_do_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class A<T> { public T? X { get; set; } }
                        public class B<T> { public T? X { get; set; } }
                        [DwarfMapper]
                        public partial class M<T> { public partial B<T> To(A<T> a); }
                        """);
        }

        [Fact]
        public void Deeply_nested_generic_arguments_do_not_crash()
        {
            NeverThrows("""
                        using System.Collections.Generic;
                        using DwarfMapper;
                        namespace Demo;
                        public class A { public Dictionary<string, List<Dictionary<int, List<string>>>>? X { get; set; } }
                        public class B { public Dictionary<string, List<Dictionary<int, List<string>>>>? X { get; set; } }
                        [DwarfMapper]
                        public partial class M { public partial B To(A a); }
                        """);
        }

        [Fact]
        public void Deeply_nested_type_declarations_do_not_crash()
        {
            var src = "using DwarfMapper;\nnamespace Demo;\n";
            const int depth = 60;
            for (var i = 0; i < depth; i++)
            {
                src += $"public class N{i} {{ public N{i + 1}? Inner {{ get; set; }} }}\n";
            }

            src += $"public class N{depth} {{ public int V {{ get; set; }} }}\n";
            src += """
                   public class Flat { public int V { get; set; } }
                   [DwarfMapper]
                   public partial class M { public partial Flat To(N0 n); }
                   """;
            NeverThrows(src);
        }

        // ─── Hostile identifiers ──────────────────────────────────────────────────
        // Emitters that build names by string concatenation break here; so do anything that indexes
        // into an identifier assuming BMP characters.

        [Fact]
        public void Verbatim_keyword_identifiers_do_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class A { public int @class { get; set; } public int @event { get; set; } }
                        public class B { public int @class { get; set; } public int @event { get; set; } }
                        [DwarfMapper]
                        public partial class @namespace { public partial B @return(A @this); }
                        """);
        }

        [Fact]
        public void Unicode_and_surrogate_pair_identifiers_do_not_crash()
        {
            // Astral-plane identifiers are legal C#; each is TWO UTF-16 code units.
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class Ａ { public int 𝓧 { get; set; } public int Ωμέγα { get; set; } }
                        public class Ｂ { public int 𝓧 { get; set; } public int Ωμέγα { get; set; } }
                        [DwarfMapper]
                        public partial class Маппер { public partial Ｂ Преобразовать(Ａ a); }
                        """);
        }

        [Fact]
        public void Very_long_identifiers_do_not_crash()
        {
            var long1 = new string('A', 2000);
            NeverThrows($$"""
                          using DwarfMapper;
                          namespace Demo;
                          public class S{{long1}} { public int X { get; set; } }
                          public class T{{long1}} { public int X { get; set; } }
                          [DwarfMapper]
                          public partial class M { public partial T{{long1}} To(S{{long1}} s); }
                          """);
        }

        // ─── Contradictory / duplicated configuration ─────────────────────────────

        [Fact]
        public void Duplicate_identical_attributes_do_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class A { public int X { get; set; } }
                        public class B { public int X { get; set; } }
                        [DwarfMapper]
                        [DwarfMapper]
                        [GenerateMap<A, B>]
                        [GenerateMap<A, B>]
                        public partial class M { }
                        """);
        }

        [Fact]
        public void Null_and_empty_string_attribute_arguments_do_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class A { public int X { get; set; } }
                        public class B { public int X { get; set; } }
                        [DwarfMapper]
                        [GenerateMap<A, B>]
                        [MapProperty<A, B>(null!, "")]
                        [MapIgnore<A, B>("")]
                        public partial class M { }
                        """);
        }

        [Fact]
        public void Inaccessible_and_write_only_members_do_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class A { private int Hidden { get; set; } public int Wo { set { } } }
                        public class B { private int Hidden { get; set; } public int Wo { set { } } }
                        [DwarfMapper]
                        public partial class M { public partial B To(A a); }
                        """);
        }

        [Fact]
        public void Abstract_and_interface_targets_do_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public interface ISrc { int X { get; } }
                        public abstract class Dst { public abstract int X { get; set; } }
                        [DwarfMapper]
                        public partial class M { public partial Dst To(ISrc s); }
                        """);
        }

        [Fact]
        public void Ref_struct_and_pointer_members_do_not_crash()
        {
            NeverThrows("""
                        using System;
                        using DwarfMapper;
                        namespace Demo;
                        public ref struct RS { public Span<byte> S; }
                        public class B { public int X { get; set; } }
                        [DwarfMapper]
                        public partial class M { public partial B To(RS r); }
                        """);
        }

        [Fact]
        public void Empty_compilation_does_not_crash()
        {
            NeverThrows("");
        }

        [Fact]
        public void Attribute_on_a_non_partial_non_class_target_does_not_crash()
        {
            NeverThrows("""
                        using DwarfMapper;
                        namespace Demo;
                        public class A { public int X { get; set; } }
                        public class B { public int X { get; set; } }
                        [DwarfMapper]
                        public enum E { One }
                        [DwarfMapper]
                        public delegate B D(A a);
                        """);
        }
    }
}
