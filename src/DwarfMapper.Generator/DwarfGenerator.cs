// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper.Generator.Model;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator;

/// <summary>
/// Incremental source generator for DwarfMapper. Resolves each [DwarfMapper]
/// partial class via sort -> pair -> prove -> emit, reporting completeness and
/// conversion-safety diagnostics, and emitting direct-assignment mapping bodies.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class DwarfGenerator : IIncrementalGenerator
{
    internal const string MarkerAttributeFullName = "DwarfMapper.DwarfMapperAttribute";

    /// <summary>
    /// Tracking name for the per-mapper extraction step. Labels the pipeline node so incremental-caching
    /// tests can assert (via <c>GeneratorDriverOptions.TrackIncrementalGeneratorSteps</c>) that an unrelated
    /// edit leaves this step <c>Cached</c>/<c>Unchanged</c>. Has no effect on generated output.
    /// </summary>
    internal const string ExtractStepName = "DwarfMapperExtract";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var mappers = context.SyntaxProvider.ForAttributeWithMetadataName(
            MarkerAttributeFullName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => MapperExtractor.Extract(ctx, ct))
            .WithTrackingName(ExtractStepName);

        context.RegisterSourceOutput(mappers, static (spc, model) => Execute(spc, model));
    }

    private static void Execute(SourceProductionContext spc, MapperClassModel model)
    {
        foreach (var diagnostic in model.Diagnostics)
        {
            spc.ReportDiagnostic(diagnostic.ToDiagnostic());
        }

        if (model.HasBlockingError)
        {
            return;
        }

        var source = MapEmitter.Emit(model);
        spc.AddSource($"{model.HintName}.g.cs", source);
    }
}
