// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>DWARF119: a <c>[MapDerivedType]</c> dispatch over a base type that can be instantiated needs an arm for
    ///     that type</b> (round 35).
    ///     <para>
    ///         A dispatch maps nothing itself: an instance either matches an arm or reaches the fallback, which throws
    ///         <c>ArgumentException</c>. An instance of the base type itself matches no derived arm. T13 made every
    ///         nested route dispatch as the direct call does, which turned such an instance under Preserve or SetNull
    ///         from "mapped as the base" into that exception; the owner ruled that the declaration, which shows the
    ///         case, is where it should be refused.
    ///     </para>
    /// </summary>
    public class DispatchBaseArmTests
    {
        private const string ConcreteAnimal = "public class Animal { public string Name { get; set; } = string.Empty; }";
        private const string AbstractAnimal = "public abstract class Animal { public string Name { get; set; } = string.Empty; }";
        private const string InterfaceAnimal = "public interface Animal { string Name { get; } }";
        private const string DogArm = "[MapDerivedType<Dog, DogDto>]";
        private const string BaseArm = "[MapDerivedType<Animal, AnimalDto>]";

        private static string Source(string mode, string animal, string arms, string sourceParameter = "Animal")
        {
            // Under an interface the dog declares the name itself; under a class it inherits it.
            var dogName = animal == InterfaceAnimal ? "public string Name { get; set; } = string.Empty; " : "";
            return $$"""
                     using DwarfMapper;
                     namespace Demo;
                     {{animal}}
                     public class Dog : Animal { {{dogName}}public int Bark { get; set; } }
                     public class AnimalDto { public string Name { get; set; } = string.Empty; }
                     public class DogDto : AnimalDto { public int Bark { get; set; } }
                     {{mode}}
                     public partial class M
                     {
                         {{arms}}
                         public partial AnimalDto MapAnimal({{sourceParameter}} a);
                     }
                     """;
        }

        [Theory]
        [InlineData(DispatchPairUnderReferenceHandlingTests.None)]
        [InlineData(DispatchPairUnderReferenceHandlingTests.Preserve)]
        [InlineData(DispatchPairUnderReferenceHandlingTests.SetNull)]
        public void A_base_that_can_be_instantiated_and_has_no_arm_is_refused_in_every_mode(string mode)
        {
            var diagnostics = GeneratorTestHarness.Run(Source(mode, ConcreteAnimal, DogArm)).Diagnostics;

            var refusal = Assert.Single(diagnostics, d => d.Id == "DWARF119");
            Assert.Equal(DiagnosticSeverity.Error, refusal.Severity);
            Assert.Equal(
                "'MapAnimal' dispatches over 'Demo.Animal', which is not abstract, and has no [MapDerivedType] arm for " +
                "'Demo.Animal' itself; an instance of exactly that type would reach the dispatch's run-time fallback and " +
                "throw. Make 'Demo.Animal' abstract if it is never instantiated, or add an arm for it",
                refusal.GetMessage(CultureInfo.InvariantCulture));
            // On the dispatch method's own line.
            Assert.Equal(10, refusal.Location.GetLineSpan().StartLinePosition.Line);
        }

        // The arm for the base may be written first or last, in either attribute form: the derived arm is still tried
        // first, because arms are ordered by specificity.
        [Theory]
        [InlineData(BaseArm + " " + DogArm)]
        [InlineData(DogArm + " " + BaseArm)]
        [InlineData(DogArm + " [MapDerivedType(typeof(Animal), typeof(AnimalDto))]")]
        public void An_arm_for_the_base_itself_answers_it(string arms)
        {
            var source = Source(DispatchPairUnderReferenceHandlingTests.None, ConcreteAnimal, arms);
            var (diagnostics, generated) = GeneratorTestHarness.Run(source);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(source);
            var dog = generated.IndexOf("global::Demo.Dog __s =>", StringComparison.Ordinal);
            var animal = generated.IndexOf("global::Demo.Animal __s =>", StringComparison.Ordinal);
            Assert.True(dog >= 0 && animal > dog, "the derived arm must come before the base arm");
        }

        // No instance of an abstract class or of an interface is of exactly that type, and `object` is left alone.
        [Theory]
        [InlineData(AbstractAnimal, "Animal")]
        [InlineData(InterfaceAnimal, "Animal")]
        [InlineData(ConcreteAnimal, "object")]
        public void A_base_with_no_instances_of_its_own_and_object_are_not_asked_for_an_arm(string animal, string sourceParameter)
        {
            var source = Source(DispatchPairUnderReferenceHandlingTests.None, animal, DogArm, sourceParameter);
            var diagnostics = GeneratorTestHarness.Run(source).Diagnostics;

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(source);
        }

        private static string Flatten(string nodeModifier, string arms)
        {
            return $$"""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public {{nodeModifier}} Node { public int Id { get; set; } public Node? Next { get; set; } }
                     public class Special : Node { public string Tag { get; set; } = ""; }
                     public class NodeDto { public int Id { get; set; } }
                     public class SpecialDto : NodeDto { public string Tag { get; set; } = ""; }
                     public class G    { public Node? Entry { get; set; } }
                     public class GDto { public List<NodeDto> Nodes { get; set; } = new(); }
                     [DwarfMapper]
                     public partial class M
                     {
                         [FlattenGraph(nameof(G.Entry), nameof(GDto.Nodes))]
                         {{arms}}
                         public partial GDto Map(G g);
                     }
                     """;
        }

        // The node dispatch of a [FlattenGraph] method has the same fallback, and the same answer.
        [Fact]
        public void A_flatten_node_base_that_can_be_instantiated_and_has_no_arm_is_refused()
        {
            var diagnostics = GeneratorTestHarness.Run(Flatten("class", "[MapDerivedType<Special, SpecialDto>]")).Diagnostics;

            var refusal = Assert.Single(diagnostics, d => d.Id == "DWARF119");
            Assert.StartsWith("[FlattenGraph] dispatches over 'Demo.Node', which is not abstract, and has no [MapDerivedType] arm for 'Demo.Node' itself",
                refusal.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.Equal(14, refusal.Location.GetLineSpan().StartLinePosition.Line);
        }

        [Theory]
        [InlineData("class", "[MapDerivedType<Special, SpecialDto>] [MapDerivedType<Node, NodeDto>]")]
        [InlineData("abstract class", "[MapDerivedType<Special, SpecialDto>]")]
        public void A_flatten_node_base_with_its_own_arm_or_no_instances_is_flattened(string nodeModifier, string arms)
        {
            var source = Flatten(nodeModifier, arms);
            var diagnostics = GeneratorTestHarness.Run(source).Diagnostics;

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(source);
        }
    }
}
