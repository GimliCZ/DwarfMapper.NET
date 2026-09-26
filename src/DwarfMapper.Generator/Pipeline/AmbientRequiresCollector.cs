// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     Collects the cross-assembly maps this compilation CONSUMES through the ambient <c>IDwarfMapper</c>
    ///     facade — auto-detected from <c>Map&lt;TDest&gt;(src)</c> / <c>Map&lt;TSrc,TDest&gt;(src)</c> call sites and
    ///     declared via <c>[UsesMap]</c> — emitted as <c>[assembly: DwarfRequiresMap(...)]</c> so the validation
    ///     root can verify every consumed pair is provided by some referenced assembly (DWARF061).
    /// </summary>
    internal static class AmbientRequiresCollector
    {
        private const string FacadeInterface = "DwarfMapper.IDwarfMapper";
        private static readonly SymbolDisplayFormat Fq = SymbolDisplayFormat.FullyQualifiedFormat;

        /// <summary>Cheap syntactic gate: any <c>receiver.Map&lt;...&gt;(...)</c> invocation.</summary>
        public static bool IsFacadeMapCall(SyntaxNode node, CancellationToken _)
        {
            return node is InvocationExpressionSyntax
                   {
                       Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax g }
                   } &&
                   g.Identifier.Text == "Map";
        }

        /// <summary>
        ///     Resolves an <c>IDwarfMapper.Map</c> call site to a (source, destination) pair, or <c>null</c> when it
        ///     is not a facade call or the source type is not a concrete named type (object / type-parameter / no
        ///     argument — left to an explicit <c>[UsesMap]</c>).
        /// </summary>
        public static (string Source, string Destination)? ExtractFacadeRequire(
            GeneratorSyntaxContext ctx,
            CancellationToken ct)
        {
            var inv = (InvocationExpressionSyntax)ctx.Node;
            if (ctx.SemanticModel.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol method)
            {
                return null;
            }

            if (!IsFacadeMap(method.Name, method.ContainingType))
            {
                return null;
            }

            ITypeSymbol? source = null;
            ITypeSymbol? destination = null;

            if (method.TypeArguments.Length == 1)
            {
                // Map<TDest>(object source): TDest explicit; source = static type of the argument expression.
                destination = method.TypeArguments[0];
                var args = inv.ArgumentList.Arguments;
                if (args.Count >= 1)
                {
                    source = ctx.SemanticModel.GetTypeInfo(args[0].Expression, ct).Type;
                }
            }
            else if (method.TypeArguments.Length == 2)
            {
                source = method.TypeArguments[0];
                destination = method.TypeArguments[1];
            }

            return ToPair(source, destination);
        }

        /// <summary>
        ///     Whether a method named <paramref name="name" /> declared on <paramref name="containingType" /> is
        ///     <c>IDwarfMapper.Map</c>.
        /// </summary>
        /// <remarks>
        ///     Takes the name and the containing type rather than the method, so both "not the facade" answers can be
        ///     asked directly. Through a compilation neither is reachable at <see cref="ExtractFacadeRequire" />:
        ///     <see cref="IsFacadeMapCall" /> admits only invocations named <c>Map</c>, and a method bound from an
        ///     invocation always has a containing type.
        /// </remarks>
        internal static bool IsFacadeMap(string name, INamedTypeSymbol? containingType)
        {
            return name == "Map" && containingType?.ToDisplayString() == FacadeInterface;
        }

        /// <summary>Reads assembly-level <c>[UsesMap]</c> (generic and non-generic) from the compilation.</summary>
        public static IReadOnlyList<(string Source, string Destination)> ReadAssemblyUsesMap(
            Compilation compilation,
            CancellationToken _)
        {
            var result = new List<(string, string)>();
            foreach (var attr in compilation.Assembly.GetAttributes())
            {
                var pair = ReadUsesMapAttribute(attr.AttributeClass, attr.ConstructorArguments);
                if (pair is not null)
                {
                    result.Add(pair.Value);
                }
            }

            return result;
        }

        /// <summary>Reads a <c>[UsesMap]</c> applied to a class (via ForAttributeWithMetadataName).</summary>
        public static IReadOnlyList<(string Source, string Destination)> ReadClassUsesMap(
            GeneratorAttributeSyntaxContext ctx,
            CancellationToken _)
        {
            var result = new List<(string, string)>();
            foreach (var attr in ctx.Attributes)
            {
                var pair = ReadUsesMapAttribute(attr.AttributeClass, attr.ConstructorArguments);
                if (pair is not null)
                {
                    result.Add(pair.Value);
                }
            }

            return result;
        }

        /// <summary>
        ///     The pair a <c>[UsesMap]</c> application declares, read from its attribute class and constructor arguments,
        ///     or <c>null</c> for any other attribute.
        /// </summary>
        /// <remarks>
        ///     Takes the class and the arguments rather than the <see cref="AttributeData" />: the class is declared
        ///     nullable, and every attribute a compilation hands the generator carries one, so the "no class" answer is
        ///     reached only through this method's own test.
        /// </remarks>
        internal static (string Source, string Destination)? ReadUsesMapAttribute(
            INamedTypeSymbol? cls,
            ImmutableArray<TypedConstant> constructorArguments)
        {
            if (cls is null)
            {
                return null;
            }

            // Generic: UsesMapAttribute<TSource, TDestination> — types are the attribute's type arguments.
            if (cls.IsGenericType &&
                cls.OriginalDefinition.ToDisplayString() ==
                "DwarfMapper.UsesMapAttribute<TSource, TDestination>")
            {
                return ToPair(cls.TypeArguments.ElementAtOrDefault(0), cls.TypeArguments.ElementAtOrDefault(1));
            }

            // Non-generic: UsesMapAttribute(Type source, Type destination).
            if (cls.ToDisplayString() == KnownNames.UsesMapFqn && constructorArguments.Length == 2)
            {
                return ToPair(constructorArguments[0].Value as ITypeSymbol,
                    constructorArguments[1].Value as ITypeSymbol);
            }

            return null;
        }

        private static (string Source, string Destination)? ToPair(ITypeSymbol? source, ITypeSymbol? destination)
        {
            if (source is null || destination is null)
            {
                return null;
            }

            if (source.TypeKind is TypeKind.TypeParameter or TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Dynamic ||
                destination.TypeKind is TypeKind.TypeParameter or TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Dynamic)
            {
                return null;
            }

            if (source.SpecialType == SpecialType.System_Object)
            {
                return null;
            }

            // Named types and arrays (of public elements) are valid ambient keys — matching the breadth the
            // registration side covers; reject only unnameable/ref-struct-ish shapes above.
            // Only effectively-public pairs are ambient (cross-assembly) — and only such types can appear in the
            // generated [assembly: DwarfRequiresMap(typeof(...))] without an accessibility error. Internal/nested
            // consumption is in-assembly; use the concrete mapper there.
            // The ONE statement of the rule, shared with facade and ambient-registration gating: a second copy lived here
            // and had already drifted from the registration side's walk once it was rewritten.
            if (!MapperExtractor.IsEffectivelyPublic(source) || !MapperExtractor.IsEffectivelyPublic(destination))
            {
                return null;
            }

            return (source.ToDisplayString(Fq), destination.ToDisplayString(Fq));
        }
    }
}
