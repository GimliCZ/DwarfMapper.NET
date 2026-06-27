// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Immutable;
using System.Linq;
using DwarfMapper.Generator.Diagnostics;
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
    /// Metadata name (with generic arity) of <see cref="DwarfMapper.GenerateMapAttribute{TSource,TTarget}"/>,
    /// used to drive the co-located pipeline: a class bearing this attribute but no <c>[DwarfMapper]</c> gets a
    /// separate generated <c>&lt;Host&gt;Mapper</c>.
    /// </summary>
    internal const string GenerateMapAttributeFullName = "DwarfMapper.GenerateMapAttribute`2";

    /// <summary>
    /// Tracking name for the per-mapper extraction step. Labels the pipeline node so incremental-caching
    /// tests can assert (via <c>GeneratorDriverOptions.TrackIncrementalGeneratorSteps</c>) that an unrelated
    /// edit leaves this step <c>Cached</c>/<c>Unchanged</c>. Has no effect on generated output.
    /// </summary>
    internal const string ExtractStepName = "DwarfMapperExtract";

    /// <summary>Tracking name for the co-located ([GenerateMap]-on-plain-class) extraction step.</summary>
    internal const string CoLocatedExtractStepName = "DwarfMapperCoLocatedExtract";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Primary pipeline: classes marked [DwarfMapper]. The mapping is emitted into the marked partial class.
        var mappers = context.SyntaxProvider.ForAttributeWithMetadataName(
            MarkerAttributeFullName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => MapperExtractor.Extract(ctx, ct))
            .WithTrackingName(ExtractStepName);

        context.RegisterSourceOutput(mappers, static (spc, model) => Execute(spc, model));

        // Co-located pipeline: a class that carries [GenerateMap<>] but is NOT a [DwarfMapper] (e.g. a DTO that
        // declares its own mapping). The mapping is emitted into a SEPARATE generated `<Host>Mapper` type, so the
        // host needs neither `partial` nor [DwarfMapper]. ExtractGenerateMapHost returns null for [DwarfMapper]
        // classes (handled above) and generic hosts, so those are filtered out before emit/aggregation.
        var coLocated = context.SyntaxProvider.ForAttributeWithMetadataName(
            GenerateMapAttributeFullName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => MapperExtractor.ExtractGenerateMapHost(ctx, ct))
            .WithTrackingName(CoLocatedExtractStepName)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutput(coLocated, static (spc, model) => Execute(spc, model));

        // Assembly-wide convenience outputs aggregated across EVERY mapper (both pipelines): the extension-method
        // facade (always) and the DI registration (only when Microsoft.Extensions.DependencyInjection is
        // referenced). Collect()'d so cross-mapper name collisions can be de-duplicated in one place; the DI
        // availability is projected to a bool so threading it through Combine() does not defeat incremental caching.
        var diAvailable = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceCollection") is not null);

        context.RegisterSourceOutput(
            mappers.Collect().Combine(coLocated.Collect()).Combine(diAvailable),
            static (spc, pair) => EmitAggregates(spc, pair.Left.Left.AddRange(pair.Left.Right), pair.Right));
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

        var (facade, facadeCollisions) = AggregateEmitter.EmitExtensions(usable);
        if (facade is not null)
        {
            spc.AddSource("DwarfMapper.Extensions.g.cs", facade);
        }
        foreach (var (sourceType, extName) in facadeCollisions)
        {
            // DWARF058 (Info): two mappers would produce the same x.ToTarget() extension, so it was dropped.
            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.DuplicateFacadeExtension, Location.None,
                $"more than one mapper maps from '{sourceType}', so the '{extName}(this {sourceType})' convenience "
                + "extension was not generated (it would be ambiguous) — call the mapper instance method, or "
                + "disable one mapper's extensions with [DwarfMapper(GenerateExtensions = false)]"));
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
