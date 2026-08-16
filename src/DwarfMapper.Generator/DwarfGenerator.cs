// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Text;
using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator;

/// <summary>
///     Incremental source generator for DwarfMapper. Resolves each [DwarfMapper]
///     partial class via sort -> pair -> prove -> emit, reporting completeness and
///     conversion-safety diagnostics, and emitting direct-assignment mapping bodies.
///     A second pipeline handles co-located [GenerateMap]-on-plain-class hosts, emitting their mapping into
///     a separate generated &lt;Host&gt;Mapper type. Both pipelines also feed the assembly-wide aggregate
///     outputs: the convenience extension facade and the AddDwarfMappers() DI registration.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class DwarfGenerator : IIncrementalGenerator
{
    internal const string MarkerAttributeFullName = KnownNames.DwarfMapperFqn;

    /// <summary>
    ///     Metadata name (with generic arity) of <see cref="DwarfMapper.GenerateMapAttribute{TSource,TTarget}" />,
    ///     used to drive the co-located pipeline: a class bearing this attribute but no <c>[DwarfMapper]</c> gets a
    ///     separate generated <c>&lt;Host&gt;Mapper</c>.
    /// </summary>
    internal const string GenerateMapAttributeFullName = "DwarfMapper.GenerateMapAttribute`2";

    /// <summary>
    ///     Tracking name for the per-mapper extraction step. Labels the pipeline node so incremental-caching
    ///     tests can assert (via <c>GeneratorDriverOptions.TrackIncrementalGeneratorSteps</c>) that an unrelated
    ///     edit leaves this step <c>Cached</c>/<c>Unchanged</c>. Has no effect on generated output.
    /// </summary>
    internal const string ExtractStepName = "DwarfMapperExtract";

    /// <summary>Tracking name for the co-located ([GenerateMap]-on-plain-class) extraction step.</summary>
    internal const string CoLocatedExtractStepName = "DwarfMapperCoLocatedExtract";

    internal const string AggregateStepName = "DwarfMapperAggregate";
    internal const string RequiresManifestStepName = "DwarfMapperRequiresManifest";
    internal const string AmbientRegistrationStepName = "DwarfMapperAmbientRegistration";

    /// <summary>Tracking name for the DWARF086 scan over hand-written manifest attributes.</summary>
    internal const string HandWrittenManifestStepName = "DwarfMapperHandWrittenManifest";

    /// <summary>Every tracked step in this generator, for the cacheability battery.</summary>
    internal static readonly string[] AllStepNames =
    {
        ExtractStepName, CoLocatedExtractStepName,
        AggregateStepName, RequiresManifestStepName, AmbientRegistrationStepName,
        HandWrittenManifestStepName,
    };

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Primary pipeline: classes marked [DwarfMapper]. The mapping is emitted into the marked partial class.
        var mappers = context.SyntaxProvider.ForAttributeWithMetadataName(
                MarkerAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => MapperExtractor.Extract(ctx, ct))
            .WithTrackingName(ExtractStepName);

        context.RegisterSourceOutput(mappers, static (spc, model) => Execute(spc, model));

        // Co-located pipeline: a class that carries [GenerateMap<>] but is NOT a [DwarfMapper] (e.g. a DTO that
        // declares its own mapping). The mapping is emitted into a SEPARATE generated `<Host>Mapper` type, so the
        // host needs neither `partial` nor [DwarfMapper]. ExtractGenerateMapHost returns null for [DwarfMapper]
        // classes (handled above) and generic hosts, so those are filtered out before emit/aggregation.
        var coLocated = context.SyntaxProvider.ForAttributeWithMetadataName(
                GenerateMapAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => MapperExtractor.ExtractGenerateMapHost(ctx, ct))
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
                if (a.AttributeClass?.ToDisplayString() != KnownNames.DwarfMapperOptionsFqn) continue;
                foreach (var na in a.NamedArguments)
                    if (na.Key == "PublicExtensions" && na.Value.Value is bool b)
                        publicExtensions = b;
            }

            return (Di: di, PublicExtensions: publicExtensions, AsmNs: SanitizeNamespace(compilation.AssemblyName));
        });

        context.RegisterSourceOutput(
            mappers.Collect().Combine(coLocated.Collect()).Combine(aggregateOptions)
                .WithTrackingName(AggregateStepName),
            static (spc, pair) => EmitAggregates(
                spc, pair.Left.Left.AddRange(pair.Left.Right), pair.Right.Di, pair.Right.PublicExtensions,
                pair.Right.AsmNs));

        // DWARF086: the Provides/Requires manifests are the generator's OUTPUT, and the validation root reads
        // them as a description of what each assembly actually registers or consumes. A hand-written entry
        // describes a map nothing produced, and it is the one falsehood the root cannot detect — so it is
        // refused here, in the compilation that wrote it. The generator's own emission carries a `.g.cs` path
        // and is excluded; see AmbientValidator.HandWrittenManifests.
        var handWrittenManifests = context.CompilationProvider
            .Select(static (compilation, _) =>
                EquatableArray.From(AmbientValidator.HandWrittenManifests(compilation)));

        context.RegisterSourceOutput(handWrittenManifests.WithTrackingName(HandWrittenManifestStepName),
            static (spc, entries) =>
            {
                foreach (var entry in entries)
                    spc.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.HandWrittenManifestAttribute,
                        entry.Location?.ToLocation() ?? Location.None,
                        entry.AttributeName));
            });

        // Ambient REQUIRES manifest: the cross-assembly maps this assembly consumes through IDwarfMapper —
        // auto-detected from Map<TDest>(src) call sites + declared via [UsesMap] — emitted as
        // [assembly: DwarfRequiresMap(...)] for the validation root to cross-check against the Provides set.
        var facadeRequires = context.SyntaxProvider
            .CreateSyntaxProvider(AmbientRequiresCollector.IsFacadeMapCall,
                AmbientRequiresCollector.ExtractFacadeRequire)
            .Where(static r => r is not null)
            .Select(static (r, _) => r!.Value);

        var assemblyUsesMap = context.CompilationProvider
            .Select(static (compilation, ct) => AmbientRequiresCollector.ReadAssemblyUsesMap(compilation, ct));

        var classUsesMap = context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownNames.UsesMapFqn,
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, ct) => AmbientRequiresCollector.ReadClassUsesMap(ctx, ct));

        var classUsesMapGeneric = context.SyntaxProvider.ForAttributeWithMetadataName(
            "DwarfMapper.UsesMapAttribute`2",
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, ct) => AmbientRequiresCollector.ReadClassUsesMap(ctx, ct));

        // This assembly's own consumed (required) ambient pairs — sorted + distinct. Reused by both the
        // Requires manifest emit and the root validation.
        var ownRequired = facadeRequires.Collect()
            .Combine(assemblyUsesMap)
            .Combine(classUsesMap.Collect())
            .Combine(classUsesMapGeneric.Collect())
            .Select(static (data, _) =>
            {
                var (((facade, assembly), classLevel), classLevelGeneric) = data;
                var all = new SortedSet<(string, string)>(AmbientValidator.OrdinalPair);
                foreach (var p in facade) all.Add(p);
                foreach (var p in assembly) all.Add(p);
                foreach (var list in classLevel)
                foreach (var p in list)
                    all.Add(p);
                foreach (var list in classLevelGeneric)
                foreach (var p in list)
                    all.Add(p);
                // EquatableArray (not raw ImmutableArray): this node feeds off assemblyUsesMap (a
                // CompilationProvider that re-runs every keystroke), so its output must be value-equatable or
                // every downstream SourceOutput re-emits on unrelated edits. See the caching regression test.
                return EquatableArray.From(all);
            });

        context.RegisterSourceOutput(ownRequired.WithTrackingName(RequiresManifestStepName),
            static (spc, pairs) => EmitRequiresManifest(spc, pairs));

        // This assembly's own provided (registered) ambient pairs — exactly what its module initializer registers.
        var ownProvided = mappers.Collect().Combine(coLocated.Collect())
            .Select(static (pair, _) =>
            {
                var usable = pair.Left.AddRange(pair.Right).Where(static m => !m.HasBlockingError).ToList();
                return ImmutableArray.CreateRange(AggregateEmitter.CollectProvidedPairs(usable));
            });

        // Root-only whole-graph view: is this the validation root (+ its AutoValidate/DI settings), and the
        // Provides/Requires of referenced assemblies.
        // NB: CompilationProvider re-runs on every keystroke, so this node's OUTPUT must be value-equatable or
        // the downstream root-validation SourceOutput re-emits Validate.g.cs on every unrelated edit. The bools
        // are value types; the pair sets are wrapped in EquatableArray (raw ImmutableArray has REFERENCE
        // equality on its backing array). Guarded by Unrelated_edit_leaves_the_root_validation_output_cached.
        var emptyPairs = EquatableArray.From(Array.Empty<(string, string)>());
        var rootInfo = context.CompilationProvider.Select((compilation, _) =>
        {
            var (isRoot, autoValidate) = AmbientValidator.GetRootConfig(compilation);
            if (!isRoot)
                return (IsRoot: false, AutoValidate: false, DiAvailable: false,
                    Provided: emptyPairs, Required: emptyPairs);
            var (provided, required) = AmbientValidator.ReadReferenced(compilation);
            var diAvailable = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.IServiceCollection") is not null;
            return (IsRoot: true, AutoValidate: autoValidate, DiAvailable: diAvailable,
                Provided: EquatableArray.From(provided), Required: EquatableArray.From(required));
        });

        context.RegisterSourceOutput(
            ownProvided.Combine(ownRequired).Combine(rootInfo).WithTrackingName(AmbientRegistrationStepName),
            static (spc, data) =>
            {
                var ((own, req), root) = data;
                if (!root.IsRoot) return;

                // DWARF061: a consumed ambient map that nothing in the graph provides.
                foreach (var (source, destination) in AmbientValidator.MissingRequires(own, req, root.Provided,
                             root.Required))
                    spc.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.RequiredMapNotProvided, Location.None, source, destination));

                // DWARF063: a pair provided by more than one assembly (first registration wins).
                foreach (var (source, destination) in AmbientValidator.AmbiguousProviders(own, root.Provided))
                    spc.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.AmbiguousAmbientProvider, Location.None, source, destination));

                // Runtime fail-fast fallback: DwarfMap.Validate() over every consumed pair (own + referenced).
                // The checked set IS the consumed link flow, so round-trip vs one-way is covered automatically.
                // AutoValidate additionally emits a [ModuleInitializer] that calls it on load.
                var consumed = req.Concat(root.Required).ToList();
                var validate = AmbientValidator.EmitValidateMethod(consumed, root.AutoValidate, own.Length > 0);
                if (validate.Length != 0)
                {
                    spc.AddNormalizedSource("DwarfMapper.Validate.g.cs", validate);

                    // DI-configurable counterpart: services.AddDwarfMappers().ValidateDwarfMaps() runs the
                    // check synchronously at the call site (chain it after the AddDwarfMappers that load providers).
                    if (root.DiAvailable)
                    {
                        var di = AmbientValidator.EmitValidateDiExtension(consumed);
                        if (di.Length != 0) spc.AddNormalizedSource("DwarfMapper.ValidateDi.g.cs", di);
                    }
                }
            });
    }

    private static void EmitRequiresManifest(SourceProductionContext spc,
        EquatableArray<(string Source, string Destination)> pairs)
    {
        if (pairs.Count == 0) return;

        var sb = new StringBuilder();
        sb.Append("// <auto-generated/>\n// SPDX-License-Identifier: GPL-2.0-only\n#nullable enable\n\n");
        foreach (var (source, destination) in pairs)
            sb.Append("[assembly: global::DwarfMapper.DwarfRequiresMap(typeof(")
                .Append(source).Append("), typeof(").Append(destination).Append("))]\n");

        spc.AddNormalizedSource("DwarfMapper.AmbientRequires.g.cs", sb.ToString());
    }

    /// <summary>
    ///     Turns an assembly name into a valid C# namespace for the generated <c>AddDwarfMappers()</c> DI class.
    ///     Emitting it in the assembly's own namespace (rather than the universally-imported
    ///     <c>Microsoft.Extensions.DependencyInjection</c>) stops the extension method colliding across assemblies
    ///     when one assembly references several mapper-bearing assemblies (CS0121). An in-assembly DI extension
    ///     still calls <c>services.AddDwarfMappers()</c> with no <c>using</c> via enclosing-namespace lookup.
    /// </summary>
    private static string SanitizeNamespace(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName)) return "DwarfMapperGenerated";

        var segments = assemblyName!.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var sb = new StringBuilder(segments[i].Length);
            foreach (var ch in segments[i]) sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');

            var s = sb.Length == 0 ? "_" : sb.ToString();
            if (char.IsDigit(s[0])) s = "_" + s;

            // A segment that is a RESERVED C# keyword is a legal assembly-name segment but not a legal namespace
            // identifier: an assembly called "Acme.class" produced `namespace Acme.class;`, which does not parse.
            // Non-identifier characters and leading digits were already handled; keywords were not. Prefixing
            // "_" keeps the transform in the same shape as the other two (and stays a valid identifier, unlike a
            // bare "@" escape inside a dotted namespace).
            // Contextual keywords (value, record, from, …) are deliberately NOT escaped — they are perfectly
            // valid identifiers, so renaming them would change the emitted namespace for no reason.
            if (SyntaxFacts.GetKeywordKind(s) != SyntaxKind.None)
                s = "_" + s;

            segments[i] = s;
        }

        return string.Join(".", segments);
    }

    /// <summary>
    ///     <c>DWARF081</c> — one logical nested pair auto-synthesized into two mappers that do not agree.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The comparison is between the auto-nested <c>__DwarfMap_Obj_*</c> entries of every mapper. Their
    ///         NAME is a hash of <c>(srcFqn, tgtFqn)</c> alone (see <c>NestedMappingRegistry.BuildMethodName</c>),
    ///         so the same name in two mappers means the same logical pair, while <c>MapMethodModel</c> is a
    ///         value-equatable record, so "same name, different record" means the two copies genuinely map
    ///         those types differently. That catches divergence from ANY cause — null-skip, null strategy,
    ///         case-insensitivity, enum strategy, a converter reserved in one mapper and not the other —
    ///         rather than only the options someone thought to enumerate.
    ///     </para>
    ///     <para>
    ///         An earlier attempt compared <c>MapperClassModel.SynthesizedMethods</c> and was reverted because
    ///         it measured empty: the auto-nested object mappers are not in that collection at all. They are
    ///         private, non-partial entries in <c>Methods</c>, which is what this reads.
    ///     </para>
    /// </remarks>
    private static void ReportDivergentSynthesizedPairs(
        SourceProductionContext spc, List<MapperClassModel> models)
    {
        if (models.Count < 2) return;

        // name -> the distinct model variants seen, and which mappers produced each.
        var byName = new Dictionary<string, List<(MapMethodModel Method, List<string> Owners)>>(
            StringComparer.Ordinal);

        foreach (var model in models)
        foreach (var method in model.Methods)
        {
            if (method.IsPartial || !GeneratedNames.IsObjectMap(method.MethodName)) continue;

            if (!byName.TryGetValue(method.MethodName, out var variants))
            {
                variants = [];
                byName[method.MethodName] = variants;
            }

            var existing = variants.Find(v => v.Method == method);
            if (existing.Owners is not null)
            {
                if (!existing.Owners.Contains(model.ClassName)) existing.Owners.Add(model.ClassName);
            }
            else
            {
                variants.Add((method, [model.ClassName]));
            }
        }

        foreach (var pair in byName.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            var variants = pair.Value;
            if (variants.Count < 2) continue;

            var first = variants[0].Method;
            var owners = variants
                .SelectMany(static v => v.Owners)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static n => n, StringComparer.Ordinal)
                .ToList();

            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.DivergentSynthesizedPair, Location.None,
                "'" + string.Join("' and '", owners) + "'",
                first.ParameterTypeFullName,
                first.ReturnTypeFullName,
                DescribeDivergence(variants.Select(static v => v.Method).ToList())));
        }
    }

    /// <summary>
    ///     Says WHAT differs between the copies, not merely that something does.
    /// </summary>
    /// <remarks>
    ///     A diagnostic reading "these disagree" would leave the reader diffing two private helpers they never
    ///     wrote and cannot see without dumping the generated output. Naming the members whose treatment
    ///     differs points straight at the option responsible.
    /// </remarks>
    private static string DescribeDivergence(List<MapMethodModel> variants)
    {
        var differing = new List<string>();

        foreach (var name in variants
                     .SelectMany(static v => v.Members.Select(static m => m.TargetName))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(static n => n, StringComparer.Ordinal))
        {
            var treatments = variants
                .Select(v => v.Members.FirstOrDefault(m =>
                    string.Equals(m.TargetName, name, StringComparison.Ordinal)))
                .Distinct()
                .Count();

            if (treatments > 1) differing.Add(name);
        }

        if (differing.Count == 0)
            // Construction, hooks or the depth signature rather than a member — still a real difference, and
            // saying "some member" would be a lie.
            return "they differ in construction or hooks rather than in a single member";

        var shown = differing.Count <= 3
            ? string.Join(", ", differing)
            : string.Join(", ", differing.Take(3)) + $", … ({differing.Count} in total)";

        return "they treat " + shown + " differently";
    }

    private static void EmitAggregates(
        SourceProductionContext spc, ImmutableArray<MapperClassModel> models, bool diAvailable, bool publicExtensions,
        string assemblyNamespace)
    {
        // Mirror Execute: a mapper with a blocking error emits no body, so it must not be referenced by the
        // facade/DI either.
        var usable = models.Where(static m => !m.HasBlockingError).ToList();
        if (usable.Count == 0) return;

        ReportDivergentSynthesizedPairs(spc, usable);

        var (facade, facadeCollisions) = AggregateEmitter.EmitExtensions(usable, publicExtensions);
        if (facade is not null) spc.AddNormalizedSource("DwarfMapper.Extensions.g.cs", facade);
        foreach (var (sourceType, extName) in facadeCollisions)
            // DWARF058 (Info): two mappers would produce the same x.ToTarget() extension, so it was dropped.
            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.DuplicateFacadeExtension, Location.None,
                $"more than one mapper maps from '{sourceType}', so the '{extName}(this {sourceType})' convenience "
                + "extension was not generated (it would be ambiguous) — call the mapper instance method, or "
                + "disable one mapper's extensions with [DwarfMapper(GenerateExtensions = false)]"));

        if (diAvailable)
        {
            var di = AggregateEmitter.EmitServiceCollection(usable, assemblyNamespace);
            if (di is not null) spc.AddNormalizedSource("DwarfMapper.ServiceCollectionExtensions.g.cs", di);
        }

        // Ambient cross-assembly registry: a module initializer self-registers this assembly's stateless,
        // public-typed create-maps into DwarfMapperRegistry, plus the [assembly: DwarfProvidesMap] manifest.
        var (ambient, unregisterable) = AggregateEmitter.EmitAmbientRegistration(usable);
        if (ambient is not null) spc.AddNormalizedSource("DwarfMapper.AmbientRegistration.g.cs", ambient);
        foreach (var mapper in unregisterable)
            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.AmbientMapperNotRegistered, Location.None, mapper));
    }

    private static void Execute(SourceProductionContext spc, MapperClassModel model)
    {
        foreach (var diagnostic in model.Diagnostics) spc.ReportDiagnostic(diagnostic.ToDiagnostic());

        if (model.HasBlockingError)
        {
            ReportCascadeSignpost(spc, model);
            return;
        }

        var source = MapEmitter.Emit(model);
        spc.AddNormalizedSource($"{model.HintName}.g.cs", source);
    }

    /// <summary>
    ///     Explains the <c>CS8795</c> wall that is about to appear, because nothing else can.
    /// </summary>
    /// <remarks>
    ///     Suppressing emission for the whole class is the right call — half-generated code would produce worse
    ///     errors than none. But it means every partial mapping method loses its implementing part at once, and
    ///     the resulting pile of <c>CS8795</c> looks identical to the OTHER cause of that wall: a project that
    ///     never wired the analyzer at all. One signpost, at the first real error's location, separates them.
    /// </remarks>
    private static void ReportCascadeSignpost(SourceProductionContext spc, MapperClassModel model)
    {
        // Distinct ids, in report order, so the message names the causes rather than repeating one id per
        // affected member. Ordinal comparison: these are ASCII identifiers, never user text.
        var ids = new List<string>();
        LocationInfo? first = null;

        foreach (var d in model.Diagnostics)
        {
            if (!d.IsError) continue;

            first ??= d.Location;
            var id = d.Descriptor.Id;
            if (!ids.Contains(id, StringComparer.Ordinal)) ids.Add(id);
        }

        if (ids.Count == 0) return;

        spc.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.NoCodeGenerated,
            first?.ToLocation() ?? Location.None,
            model.ClassName,
            string.Join(", ", ids)));
    }
}
