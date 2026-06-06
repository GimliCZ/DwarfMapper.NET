// SPDX-License-Identifier: GPL-2.0-only
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator;

/// <summary>
/// Incremental source generator entry point for DwarfMapper. The full
/// sort/pair/prove/emit mapping pipeline is added in a follow-up task; this
/// scaffold registers the attribute-driven pipeline and currently emits nothing.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class DwarfGenerator : IIncrementalGenerator
{
    internal const string MarkerAttributeFullName = "DwarfMapper.DwarfMapperAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var mappers = context.SyntaxProvider.ForAttributeWithMetadataName(
            MarkerAttributeFullName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, _) => ctx.TargetSymbol.Name);

        // Placeholder sink: the mapping pipeline is implemented in a follow-up task.
        context.RegisterSourceOutput(mappers, static (spc, mapperName) => _ = mapperName);
    }
}
