// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Text;
using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     One hand-written cross-assembly manifest attribute: its simple name (for the message) and where it was
    ///     written (for the squiggle). A record rather than a tuple because it travels through an incremental
    ///     pipeline node, where reference equality on the carrier would re-report the diagnostic on every keystroke.
    /// </summary>
    internal sealed record HandWrittenManifest(string AttributeName, LocationInfo? Location);

    /// <summary>
    ///     Whole-graph cross-assembly linkage validation, performed ONLY in the compilation marked
    ///     <c>[assembly: DwarfMapperValidationRoot]</c> (the one that references every provider and consumer). It
    ///     aggregates the <c>Provides</c> / <c>Requires</c> manifests of all referenced assemblies with this
    ///     compilation's own provided/required pairs, and reports DWARF061 for any required ambient map that nothing
    ///     provides — turning the otherwise-runtime "no map registered" failure into a compile-time error.
    /// </summary>
    internal static class AmbientValidator
    {
        private static readonly SymbolDisplayFormat Fq = SymbolDisplayFormat.FullyQualifiedFormat;

        /// <summary>
        ///     Ordinal ordering for (source, destination) pairs — the default tuple comparer routes to
        ///     culture-sensitive <c>string.CompareTo</c>, which would make the emitted check order, message order, and
        ///     diagnostic order vary by machine culture (breaking deterministic-build reproducibility).
        /// </summary>
        public static readonly IComparer<(string, string)> OrdinalPair =
            Comparer<(string, string)>.Create((a, b) =>
            {
                var c = string.CompareOrdinal(a.Item1, b.Item1);
                return c != 0 ? c : string.CompareOrdinal(a.Item2, b.Item2);
            });

        /// <summary>
        ///     Reads <c>[assembly: DwarfMapperValidationRoot]</c> and its <c>AutoValidate</c> setting. <c>IsRoot</c> is
        ///     false when the attribute is absent.
        /// </summary>
        public static (bool IsRoot, bool AutoValidate) GetRootConfig(Compilation compilation)
        {
            foreach (var a in compilation.Assembly.GetAttributes())
            {
                if (!KnownNames.IsAttributeClass(a.AttributeClass, KnownNames.ValidationRootFqn))
                {
                    continue;
                }

                // AutoValidate is the attribute's only settable property and a bool, so a set value is always a bool
                // constant: asked this way, no branch tests a key or a type that no application has.
                var autoValidate = MapperExtractor.TryGetNamedArgument(a.NamedArguments, "AutoValidate", out var value) &&
                                   Equals(value.Value, true);

                return (true, autoValidate);
            }

            return (false, false);
        }

        /// <summary>
        ///     Reads the <c>DwarfProvidesMap</c> / <c>DwarfRequiresMap</c> manifests from every referenced assembly
        ///     (these live in metadata as assembly attributes — readable; the generator cannot see its OWN
        ///     not-yet-emitted manifests, so the root supplies its own provided/required separately).
        /// </summary>
        // Provided carries the PROVIDING ASSEMBLY, not just the pair. DWARF063's whole job is to say a pair is
        // provided more than once, and a reader cannot act on that without knowing WHICH assemblies collided —
        // the diagnostic is reported with Location.None, so the message text is the only information there is.
        // Keeping the identity here also lets AmbiguousProviders count DISTINCT providers rather than raw
        // occurrences, which is what its message has always claimed to mean.
        public static (ImmutableArray<(string Source, string Destination, string Assembly)> Provided,
            ImmutableArray<(string Source, string Destination)> Required) ReadReferenced(Compilation compilation)
        {
            var provided = ImmutableArray.CreateBuilder<(string, string, string)>();
            var required = ImmutableArray.CreateBuilder<(string, string)>();

            foreach (var asm in compilation.SourceModule.ReferencedAssemblySymbols)
            foreach (var a in asm.GetAttributes())
                switch (KnownNames.AttributeClassName(a.AttributeClass))
                {
                    case KnownNames.DwarfProvidesMapFqn:
                        if (ReadPair(a) is { } p)
                        {
                            provided.Add((p.Item1, p.Item2, asm.Name));
                        }

                        break;

                    case KnownNames.DwarfRequiresMapFqn:
                        if (ReadPair(a) is { } r)
                        {
                            required.Add(r);
                        }

                        break;
                }

            return (provided.ToImmutable(), required.ToImmutable());
        }

        /// <summary>
        ///     The manifest attributes THIS compilation carries in hand-written source — each one a DWARF086.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Scoped to <c>compilation.Assembly</c> on purpose: a referenced assembly's manifest is metadata
        ///         written by ITS build and is not this compilation's to judge, and refusing it would make every
        ///         consumer of a correctly-generated provider red.
        ///     </para>
        ///     <para>
        ///         The generator's own emission is excluded by
        ///         <see cref="GeneratedSourceExtensions.IsGeneratorAuthored" /> rather than by relying on Roslyn
        ///         handing generators the pre-generation compilation. That happens to be true today — see this
        ///         type's <see cref="ReadReferenced" /> remarks — but it is an ordering property of the host, and a
        ///         refusal whose correctness rests on the check never meeting its own output is one host change
        ///         away from failing every multi-assembly build.
        ///     </para>
        /// </remarks>
        public static IReadOnlyList<HandWrittenManifest> HandWrittenManifests(Compilation compilation)
        {
            var found = new List<HandWrittenManifest>();

            foreach (var a in compilation.Assembly.GetAttributes())
            {
                var name = KnownNames.AttributeClassName(a.AttributeClass);
                if (name != KnownNames.DwarfProvidesMapFqn && name != KnownNames.DwarfRequiresMapFqn)
                {
                    continue;
                }

                var reference = a.ApplicationSyntaxReference;
                if (GeneratedSourceExtensions.IsGeneratorAuthored(reference?.SyntaxTree))
                {
                    continue;
                }

                found.Add(new HandWrittenManifest(
                    a.AttributeClass!.Name,
                    LocationInfo.From(Location.Create(reference!.SyntaxTree, reference.Span))));
            }

            return found;
        }

        private static (string Source, string Destination)? ReadPair(AttributeData a)
        {
            if (a.ConstructorArguments.Length != 2)
            {
                return null;
            }

            if (a.ConstructorArguments[0].Value is not ITypeSymbol source ||
                a.ConstructorArguments[1].Value is not ITypeSymbol destination)
            {
                return null;
            }

            return (source.ToDisplayString(Fq), destination.ToDisplayString(Fq));
        }

        /// <summary>
        ///     The required ambient pairs (this compilation's own + all referenced) that nothing in the graph
        ///     provides — each becomes a DWARF061. Deterministically ordered.
        /// </summary>
        public static IReadOnlyList<(string Source, string Destination)> MissingRequires(
            IEnumerable<(string Source, string Destination)> ownProvided,
            IEnumerable<(string Source, string Destination)> ownRequired,
            IEnumerable<(string Source, string Destination, string Assembly)> referencedProvided,
            IEnumerable<(string Source, string Destination)> referencedRequired)
        {
            var provided = new HashSet<(string, string)>();
            foreach (var p in ownProvided) provided.Add(p);
            foreach (var p in referencedProvided) provided.Add((p.Source, p.Destination));

            var required = new SortedSet<(string, string)>(OrdinalPair);
            foreach (var r in ownRequired) required.Add(r);
            foreach (var r in referencedRequired) required.Add(r);

            var missing = new List<(string, string)>();
            foreach (var r in required)
                if (!provided.Contains(r))
                {
                    missing.Add(r);
                }

            return missing;
        }

        /// <summary>
        ///     Pairs provided by more than one DISTINCT assembly in the graph (this compilation + referenced), each
        ///     with the names of the assemblies providing it — the ambient registry keeps the first registration and
        ///     ignores the rest, so these become DWARF063 warnings.
        /// </summary>
        /// <remarks>
        ///     This used to count OCCURRENCES: every entry incremented a counter, so a pair listed twice by a single
        ///     assembly — or a pair reaching the counter twice because one component appeared twice in a reference
        ///     closure — tripped a diagnostic whose text says "more than one ASSEMBLY provides". The message and the
        ///     implementation disagreed, and the message was the correct one. Counting distinct providing assemblies
        ///     is what it always meant, and it is what makes naming them in the message possible.
        /// </remarks>
        public static IReadOnlyList<(string Source, string Destination, string Providers)> AmbiguousProviders(
            IEnumerable<(string Source, string Destination)> ownProvided,
            IEnumerable<(string Source, string Destination, string Assembly)> referencedProvided,
            string ownAssemblyName)
        {
            var providers = new Dictionary<(string, string), SortedSet<string>>();

            void Add((string, string) pair, string assembly)
            {
                if (!providers.TryGetValue(pair, out var set))
                {
                    set = new SortedSet<string>(StringComparer.Ordinal);
                    providers[pair] = set;
                }

                set.Add(assembly);
            }

            foreach (var p in ownProvided) Add(p, ownAssemblyName);
            foreach (var p in referencedProvided) Add((p.Source, p.Destination), p.Assembly);

            var ordered = new SortedSet<(string, string)>(OrdinalPair);
            foreach (var kv in providers)
                if (kv.Value.Count > 1)
                {
                    ordered.Add(kv.Key);
                }

            var result = new List<(string, string, string)>();
            foreach (var pair in ordered)
                result.Add((pair.Item1, pair.Item2, string.Join(", ", providers[pair])));

            return result;
        }

        /// <summary>
        ///     Emits the root-only <c>DwarfMapper.DwarfMap.Validate()</c> — a hard-coded (reflection-free) runtime
        ///     fail-fast that throws <c>DwarfMapValidationException</c> if any ambient map the app consumes is not
        ///     registered in the live registry (defense-in-depth against trimming / unloaded assemblies). The checked
        ///     set is exactly what the graph consumes, so a pair used both ways is validated both ways automatically.
        ///     When <paramref name="autoValidate" /> is set, a <c>[ModuleInitializer]</c> calls <c>Validate()</c> on
        ///     load. Returns the empty string when nothing is consumed.
        /// </summary>
        public static string EmitValidateMethod(
            IEnumerable<(string Source, string Destination)> required,
            bool autoValidate,
            bool hasOwnRegistration)
        {
            var pairs = new SortedSet<(string, string)>(OrdinalPair);
            foreach (var r in required)
                pairs.Add(r);
            if (pairs.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.Append("// <auto-generated/>\n// SPDX-License-Identifier: GPL-2.0-only\n#nullable enable\n\n");
            sb.AppendLine("namespace DwarfMapper;");
            sb.AppendLine();
            sb.AppendLine(
                "/// <summary>Generated in the validation-root assembly: a runtime fail-fast check that every ambient");
            sb.AppendLine("/// map the application consumes is actually registered (defense-in-depth against trimming or");
            sb.AppendLine(
                "/// not-yet-loaded assemblies). Call it once at startup, via <c>services.ValidateDwarfMaps()</c>,");
            sb.AppendLine("/// or set <c>[DwarfMapperValidationRoot(AutoValidate = true)]</c>.</summary>");
            sb.AppendLine("public static class DwarfMap");
            sb.AppendLine("{");
            sb.AppendLine(
                "    /// <summary>Throws <see cref=\"DwarfMapValidationException\"/> if any consumed ambient map is missing.</summary>");
            sb.AppendLine("    public static void Validate()");
            sb.AppendLine("    {");
            if (hasOwnRegistration)
                // Force this assembly's own ambient registration first, so Validate() is independent of
                // module-initializer ordering (the AutoValidate initializer may otherwise run before it). __Register
                // is guarded run-once (thread-safe), so this never double-registers or pollutes the ambiguity set.
            {
                sb.AppendLine("        global::DwarfMapper.Generated.__DwarfMapperAmbientRegistration.__Register();");
            }

            sb.AppendLine("        var __missing = new global::System.Collections.Generic.List<string>();");
            foreach (var (source, destination) in pairs)
                sb.Append("        if (!global::DwarfMapper.DwarfMapperRegistry.IsProvided(typeof(").Append(source)
                    .Append("), typeof(").Append(destination).Append("))) __missing.Add(\"")
                    .Append(source).Append(" -> ").Append(destination).Append("\");\n");
            sb.AppendLine("        if (__missing.Count > 0)");
            sb.AppendLine("            throw new global::DwarfMapper.DwarfMapValidationException(");
            sb.AppendLine(
                "                \"DwarfMapper: required ambient map(s) not registered at runtime: \" + global::System.String.Join(\", \", __missing));");
            sb.AppendLine("    }");
            if (autoValidate)
            {
                sb.AppendLine();
                sb.AppendLine(
                    "    /// <summary>Auto-invoked on root-module load via <c>[DwarfMapperValidationRoot(AutoValidate = true)]</c>.</summary>");
                sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
                sb.AppendLine("    internal static void __DwarfAutoValidate() => Validate();");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        ///     Emits an <c>IServiceCollection.ValidateDwarfMaps()</c> extension in the validation-root assembly that
        ///     calls <c>DwarfMap.Validate()</c> when invoked (typically right after <c>AddDwarfMappers()</c>, so the
        ///     check runs synchronously at the call site (during ConfigureServices) — the ordering-safe counterpart to
        ///     <c>AutoValidate</c>). Returns the empty string when nothing is consumed. Emitted only when the DI
        ///     abstractions are referenced.
        /// </summary>
        public static string EmitValidateDiExtension(IEnumerable<(string Source, string Destination)> required)
        {
            var pairs = new SortedSet<(string, string)>(OrdinalPair);
            foreach (var r in required)
                pairs.Add(r);
            if (pairs.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.Append("// <auto-generated/>\n// SPDX-License-Identifier: GPL-2.0-only\n#nullable enable\n\n");
            sb.AppendLine("namespace Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>DI-friendly ambient-map validation for the validation-root assembly.</summary>");
            sb.AppendLine("public static class DwarfMapValidationServiceCollectionExtensions");
            sb.AppendLine("{");
            sb.AppendLine(
                "    /// <summary>Runs <c>DwarfMap.Validate()</c> immediately (fail-fast) and returns <paramref name=\"services\"/>.");
            sb.AppendLine(
                "    /// Chain after <c>AddDwarfMappers()</c> so provider registration has run: <c>services.AddDwarfMappers().ValidateDwarfMaps();</c></summary>");
            sb.AppendLine(
                "    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection ValidateDwarfMaps(");
            sb.AppendLine("        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            sb.AppendLine("    {");
            sb.AppendLine("        global::DwarfMapper.DwarfMap.Validate();");
            sb.AppendLine("        return services;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
