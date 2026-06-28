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
/// A second pipeline handles co-located [GenerateMap]-on-plain-class hosts, emitting their mapping into
/// a separate generated &lt;Host&gt;Mapper type. Both pipelines also feed the assembly-wide aggregate
/// outputs: the convenience extension facade and the AddDwarfMappers() DI registration.
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
        // referenced). Collect()'d so cross-mapper name collisions can be de-duplicated in one place.
        // Assembly-wide options projected to value-equatable bools (keeps incremental caching): whether DI is
        // referenced, and whether [assembly: DwarfMapperOptions(PublicExtensions = true)] opts the facade public.
        var aggregateOptions = context.CompilationProvider.Select(static (compilation, _) =>
        {
            var di = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.IServiceCollection") is not null;
            var publicExtensions = false;
            foreach (var a in compilation.Assembly.GetAttributes())
            {
                if (a.AttributeClass?.ToDisplayString() != "DwarfMapper.DwarfMapperOptionsAttribute")
                {
                    continue;
                }
                foreach (var na in a.NamedArguments)
                {
                    if (na.Key == "PublicExtensions" && na.Value.Value is bool b)
                    {
                        publicExtensions = b;
                    }
                }
            }
            return (Di: di, PublicExtensions: publicExtensions, AsmNs: SanitizeNamespace(compilation.AssemblyName));
        });

        context.RegisterSourceOutput(
            mappers.Collect().Combine(coLocated.Collect()).Combine(aggregateOptions),
            static (spc, pair) => EmitAggregates(
                spc, pair.Left.Left.AddRange(pair.Left.Right), pair.Right.Di, pair.Right.PublicExtensions, pair.Right.AsmNs));
    }

    /// <summary>
    /// Turns an assembly name into a valid C# namespace for the generated <c>AddDwarfMappers()</c> DI class.
    /// Emitting it in the assembly's own namespace (rather than the universally-imported
    /// <c>Microsoft.Extensions.DependencyInjection</c>) stops the extension method colliding across assemblies
    /// when one assembly references several mapper-bearing assemblies (CS0121). An in-assembly DI extension
    /// still calls <c>services.AddDwarfMappers()</c> with no <c>using</c> via enclosing-namespace lookup.
    /// </summary>
    private static string SanitizeNamespace(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return "DwarfMapperGenerated";
        }

        var segments = assemblyName!.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var sb = new System.Text.StringBuilder(segments[i].Length);
            foreach (var ch in segments[i])
            {
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            }

            var s = sb.Length == 0 ? "_" : sb.ToString();
            segments[i] = char.IsDigit(s[0]) ? "_" + s : s;
        }

        return string.Join(".", segments);
    }

    private static void EmitAggregates(
        SourceProductionContext spc, ImmutableArray<MapperClassModel> models, bool diAvailable, bool publicExtensions,
        string assemblyNamespace)
    {
        // Mirror Execute: a mapper with a blocking error emits no body, so it must not be referenced by the
        // facade/DI either.
        var usable = models.Where(static m => !m.HasBlockingError).ToList();
        if (usable.Count == 0)
        {
            return;
        }

        var (facade, facadeCollisions) = AggregateEmitter.EmitExtensions(usable, publicExtensions);
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
            var di = AggregateEmitter.EmitServiceCollection(usable, assemblyNamespace);
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
