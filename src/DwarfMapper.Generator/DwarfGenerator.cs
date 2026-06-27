// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Immutable;
using System.Linq;
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

        // Assembly-wide convenience outputs aggregated across every mapper: the extension-method facade
        // (always) and the DI registration (only when Microsoft.Extensions.DependencyInjection is referenced).
        // Collect()'d so cross-mapper name collisions can be de-duplicated in one place; the DI availability
        // is projected to a bool so threading it through Combine() does not defeat incremental caching.
        var diAvailable = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceCollection") is not null);

        context.RegisterSourceOutput(
            mappers.Collect().Combine(diAvailable),
            static (spc, pair) => EmitAggregates(spc, pair.Left, pair.Right));
    }

    private static void EmitAggregates(
        SourceProductionContext spc, ImmutableArray<MapperClassModel> models, bool diAvailable)
    {
        // Mirror Execute: a mapper with a blocking error emits no body, so it must not be referenced by the
        // facade/DI either.
        var usable = models.Where(static m => !m.HasBlockingError).ToList();
        if (usable.Count == 0)
        {
            return;
        }

        var facade = AggregateEmitter.EmitExtensions(usable);
        if (facade is not null)
        {
            spc.AddSource("DwarfMapper.Extensions.g.cs", facade);
        }

        if (diAvailable)
        {
            var di = AggregateEmitter.EmitServiceCollection(usable);
            if (di is not null)
            {
                spc.AddSource("DwarfMapper.ServiceCollectionExtensions.g.cs", di);
            }
        }
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
