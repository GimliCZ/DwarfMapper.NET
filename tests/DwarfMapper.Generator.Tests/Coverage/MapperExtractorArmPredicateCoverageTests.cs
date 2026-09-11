// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Coverage suite for MapperExtractor's small named arm predicates that no generator-surface fixture can
// drive both ways. Technique: unit (Roslyn symbol-based), same pattern as BlittableProofCoverageTests.
namespace DwarfMapper.Generator.Tests.Coverage
{
    /// <summary>
    ///     Unit tests for <c>MapperExtractor.NestedRegistryMarksPairCustomized</c> — the null-conditional
    ///     <c>HandleCollectionConversion</c> carved it out of, because <c>ConversionRequest.NestedRegistry</c>
    ///     is built once per <c>Extract</c> call and threaded non-null through every real resolution path,
    ///     so no <c>[DwarfMapper]</c> fixture can ever present a <see langword="null" /> registry here. Both
    ///     arms of the null-conditional are pinned directly instead.
    /// </summary>
    public class MapperExtractorArmPredicateCoverageTests
    {
        private static (Compilation Compilation, IReadOnlyDictionary<string, INamedTypeSymbol> Types)
            Compile(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var refs = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .Append(MetadataReference.CreateFromFile(typeof(DwarfMapperAttribute).Assembly.Location));

            var compilation = CSharpCompilation.Create(
                "ArmPredicateTestAsm_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    tree
                },
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var types = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            foreach (var decl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is { } named)
                {
                    types[named.Name] = named;
                }
            }

            return (compilation, types);
        }

        [Fact]
        public void Null_registry_answers_false_without_calling_it()
        {
            var (_, types) = Compile("namespace T { public class Src { } public class Dst { } }");
            Assert.False(MapperExtractor.NestedRegistryMarksPairCustomized(null, types["Src"], types["Dst"]));
        }

        [Fact]
        public void Registry_without_a_customization_rule_answers_false()
        {
            var (_, types) = Compile("namespace T { public class Src { } public class Dst { } }");
            var registry = new NestedMappingRegistry();
            Assert.False(MapperExtractor.NestedRegistryMarksPairCustomized(registry, types["Src"], types["Dst"]));
        }

        [Fact]
        public void Registry_with_a_matching_customization_rule_answers_true()
        {
            var (_, types) = Compile("namespace T { public class Src { } public class Dst { } }");
            var registry = new NestedMappingRegistry();
            registry.SetPairCustomizationRule((src, tgt) =>
                SymbolEqualityComparer.Default.Equals(src, types["Src"]) &&
                SymbolEqualityComparer.Default.Equals(tgt, types["Dst"])
                    ? ("Src", "Dst")
                    : null);
            Assert.True(MapperExtractor.NestedRegistryMarksPairCustomized(registry, types["Src"], types["Dst"]));
        }
    }
}
