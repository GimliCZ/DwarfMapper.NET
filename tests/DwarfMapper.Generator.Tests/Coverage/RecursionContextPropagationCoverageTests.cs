// SPDX-License-Identifier: GPL-2.0-only

// Coverage suite for MapperExtractor.Phases.cs's post-resolution passes that thread a DwarfRefContext into a
// CONSTRUCTOR ARGUMENT, or into a member that calls a [MapDerivedType] dispatch method. Every recursion fixture
// the suite had routed the cycle through settable members, so the ctor-arg halves of these passes — separate
// loops that read almost identically to the member halves — had never run:
//   - SynthesizePreserveDispatchWrappers: a caller of a public dispatch method is redirected to the wrapper;
//   - MarkRecursionCapableCallers: a self-recursive declared method's ctor args (self-call, another recursive
//     declared method, a recursion-capable synthesized pair), and a non-recursive caller's ctor args;
//   - the call graph's overload expansion, when the recursive declared methods share one name;
//   - ReportSameSourceSignatureCollisions' "two identical declared partials" exemption.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class RecursionContextPropagationCoverageTests
    {
        private const string CyclicAnimals = """
                                             public abstract class Animal { public string Name { get; set; } = ""; public Animal? Friend { get; set; } }
                                             public class Dog : Animal { public string Breed { get; set; } = ""; }
                                             public class AnimalDto { public string Name { get; set; } = ""; public AnimalDto? Friend { get; set; } }
                                             public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                                             """;

        [Fact]
        public void Preserve_ctor_arg_calling_a_recursive_dispatch_method_goes_through_the_dispatch_wrapper()
        {
            var src = "using DwarfMapper;\nnamespace Demo;\n" + CyclicAnimals + """
                                                                                public class Zoo { public Animal Star { get; set; } = new Dog(); }
                                                                                public class ZooDto { public ZooDto(AnimalDto star) { Star = star; } public AnimalDto Star { get; } }
                                                                                [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                                                                                public partial class M
                                                                                {
                                                                                    [MapDerivedType<Dog, DogDto>]
                                                                                    public partial AnimalDto ToDto(Animal a);
                                                                                    public partial DogDto ToDog(Dog d);
                                                                                    public partial ZooDto ToZoo(Zoo z);
                                                                                }
                                                                                """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            // Calling ToDto directly would open a fresh identity map per call and lose the shared graph.
            Assert.Contains("star: __DwarfMap_Disp_global__Demo_Animal_global__Demo_AnimalDto_", generated, StringComparison.Ordinal);
            Assert.Contains("(z.Star!, __dwarf_ctx, 0));", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Preserve_dispatch_method_is_wrapped_even_without_a_cycle()
        {
            // Under Preserve every auto-nested object mapper is forced recursion-capable for uniform identity
            // tracking, so an arm with no cycle still needs the shared context and the dispatch gets a wrapper.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   public partial AnimalDto Map(Animal a);
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("__DwarfMap_Disp_global__Demo_Animal_global__Demo_AnimalDto_", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Self_recursive_declared_method_redirects_its_ctor_args_to_depth_companions()
        {
            // next: a self-call; other: a DIFFERENT self-recursive declared method (Leaf reaches Node back).
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Node { public int V { get; set; } public Node? Next { get; set; } public Leaf? Other { get; set; } }
                               public class NodeDto { public NodeDto(int v, NodeDto? next, LeafDto? other) { V = v; Next = next; Other = other; } public int V { get; } public NodeDto? Next { get; } public LeafDto? Other { get; } }
                               public class Leaf { public int W { get; set; } public Node? Back { get; set; } public Leaf? Self { get; set; } }
                               public class LeafDto { public int W { get; set; } public NodeDto? Back { get; set; } public LeafDto? Self { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   public partial NodeDto MapNode(Node n);
                                   public partial LeafDto MapLeaf(Leaf l);
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("next: __DwarfMap_Depth_MapNode(n.Next!, __dwarf_ctx, 0),", generated, StringComparison.Ordinal);
            Assert.Contains("other: __DwarfMap_Depth_MapLeaf(n.Other!, ctx, depth + 1));", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Self_recursive_declared_method_threads_ctx_into_a_recursion_capable_synthesized_ctor_arg()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Chain { public int V { get; set; } public Chain? Next { get; set; } }
                               public class ChainDto { public int V { get; set; } public ChainDto? Next { get; set; } }
                               public class Node { public int V { get; set; } public Node? Next { get; set; } public Chain? C { get; set; } }
                               public class NodeDto { public NodeDto(int v, NodeDto? next, ChainDto? c) { V = v; Next = next; C = c; } public int V { get; } public NodeDto? Next { get; } public ChainDto? C { get; } }
                               [DwarfMapper(AutoNest = true)]
                               public partial class M { public partial NodeDto MapNode(Node n); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("c: __DwarfMap_Obj_global__Demo_Chain_global__Demo_ChainDto_", generated, StringComparison.Ordinal);
            Assert.Contains("(n.C!, ctx, depth + 1));", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Non_recursive_caller_threads_ctx_into_its_ctor_args()
        {
            // root: a recursion-capable synthesized pair; declared: a self-recursive declared method's companion.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Chain { public int V { get; set; } public Chain? Next { get; set; } }
                               public class ChainDto { public int V { get; set; } public ChainDto? Next { get; set; } }
                               public class Holder { public Chain Root { get; set; } = new(); public Chain Declared { get; set; } = new(); }
                               public class HolderDto { public HolderDto(ChainDto root, NodeDto declared) { Root = root; Declared = declared; } public ChainDto Root { get; } public NodeDto Declared { get; } }
                               public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                               [DwarfMapper(AutoNest = true)]
                               public partial class M
                               {
                                   public partial HolderDto MapHolder(Holder h);
                                   public partial NodeDto MapNode(Chain c);
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("root: __DwarfMap_Obj_global__Demo_Chain_global__Demo_ChainDto_", generated, StringComparison.Ordinal);
            Assert.Contains("declared: __DwarfMap_Depth_MapNode(h.Declared!, __dwarf_ctx, 0));", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Two_identical_declared_partials_are_the_compilers_error_not_a_collision_diagnostic()
        {
            // Two identical partial DEFINITIONS are CS0756 in the consumer's own file; announcing DWARF094 on top
            // would blame the generator for a mistake it did not make (see ReportSameSourceSignatureCollisions).
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } }
                               public class Dst { public int A { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   public partial Dst Map(Src s);
                                   public partial Dst Map(Src s);
                               }
                               """;

            GeneratorAssert.DoesNotReport(src, "DWARF094");
            GeneratorAssert.DoesNotReport(src, "DWARF060");
        }
    }
}
