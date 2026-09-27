// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Text;
using System.Threading;
using DwarfMapper.Generator.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     A pair the ambient registration registers, with the expression it runs for it written over the parameters
    ///     <c>source</c> (and <c>destination</c> for an update-into map). See
    ///     <see cref="AggregateEmitter.InterceptTargets" />.
    /// </summary>
    internal readonly record struct InterceptTarget(string Source, string Destination, bool IsUpdate, string Body);

    /// <summary>
    ///     One <c>Dwarf.Map</c> call site: the pair it names and where it is, as the two values an
    ///     <c>[InterceptsLocation]</c> attribute carries. Strings and an int only, so the incremental cache compares
    ///     it by value.
    /// </summary>
    internal readonly record struct DwarfMapCallSite(
        string Source,
        string Destination,
        bool IsUpdate,
        int LocationVersion,
        string LocationData);

    /// <summary>
    ///     Round 31 T26: binds a <c>Dwarf.Map</c> call at compile time when the calling assembly itself registers the
    ///     pair, by emitting a C# interceptor that runs the registration's own expression on the registration's own
    ///     mapper instance. Everything else - a pair from another assembly, a runtime-type dispatch - is left to the
    ///     call's run-time body, which resolves through the registry exactly as <c>IDwarfMapper</c> does.
    /// </summary>
    /// <remarks>
    ///     Only the static <c>DwarfMapper.Dwarf</c> class is ever bound, never <c>IDwarfMapper</c>: a call through the
    ///     interface may reach an implementation the application injected (a decorator, a test double), and binding it
    ///     statically would silently bypass that. A static call has no receiver to bypass.
    /// </remarks>
    internal static class DwarfCallSites
    {
        /// <summary>The runtime class whose <c>Map</c> calls are bound.</summary>
        internal const string DwarfClass = "DwarfMapper.Dwarf";

        /// <summary>
        ///     The namespace the interceptors are emitted into - the one the package's <c>build/DwarfMapper.props</c>
        ///     adds to <c>InterceptorsNamespaces</c>. The compiler refuses an interceptor anywhere else.
        /// </summary>
        internal const string InterceptorsNamespace = "DwarfMapper.Generated";

        /// <summary>Cheap syntactic gate: an invocation of something named <c>Map</c>.</summary>
        public static bool IsCandidate(SyntaxNode node, CancellationToken _)
        {
            return node is InvocationExpressionSyntax inv && inv.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.Identifier.Text == "Map",
                SimpleNameSyntax name => name.Identifier.Text == "Map",
                _ => false
            };
        }

        /// <summary>
        ///     The call site of a <c>Dwarf.Map</c> call that can be bound, or <c>null</c>: not that method, a type
        ///     argument that is not a concrete public type (a type parameter, <c>object</c>, an annotated nullable
        ///     reference - its signature would not match the interceptor's), or a location the compiler cannot
        ///     intercept.
        /// </summary>
        public static DwarfMapCallSite? Extract(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            var inv = (InvocationExpressionSyntax)ctx.Node;
            if (ctx.SemanticModel.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol method ||
                method.Name != "Map" ||
                method.ContainingType?.ToDisplayString() != DwarfClass ||
                method.TypeArguments.Length != 2)
            {
                return null;
            }

            var source = method.TypeArguments[0];
            var destination = method.TypeArguments[1];
            if (IsAnnotatedReference(source) || IsAnnotatedReference(destination))
            {
                return null;
            }

            var pair = AmbientRequiresCollector.ToPair(source, destination);
            var location = ctx.SemanticModel.GetInterceptableLocation(inv, ct);
            if (pair is null || location is null)
            {
                return null;
            }

            return new DwarfMapCallSite(pair.Value.Source, pair.Value.Destination, method.Parameters.Length == 2,
                location.Version, location.Data);
        }

        private static bool IsAnnotatedReference(ITypeSymbol type)
        {
            return !type.IsValueType && type.NullableAnnotation == NullableAnnotation.Annotated;
        }

        /// <summary>
        ///     The interceptor source for every call in <paramref name="calls" /> whose pair
        ///     <paramref name="targets" /> registers and no referenced assembly also provides, or <c>null</c> when none
        ///     does. A pair a referenced assembly provides too is left to the registry: which of the two registrations
        ///     the registry answers with depends on assembly load order, so binding it here could pick the other one.
        /// </summary>
        public static string? EmitInterceptors(
            ImmutableArray<DwarfMapCallSite> calls,
            EquatableArray<InterceptTarget> targets,
            EquatableArray<(string Source, string Destination, string Assembly)> referencedProvided)
        {
            var byPair = new Dictionary<(string, string, bool), InterceptTarget>();
            foreach (var t in targets)
                byPair[(t.Source, t.Destination, t.IsUpdate)] = t;

            var providedElsewhere = new HashSet<(string, string)>();
            foreach (var p in referencedProvided)
                providedElsewhere.Add((p.Source, p.Destination));

            var bound = new SortedDictionary<(bool, string, string), List<DwarfMapCallSite>>(PairOrder.Instance);
            foreach (var call in calls)
            {
                if (!byPair.ContainsKey((call.Source, call.Destination, call.IsUpdate)) ||
                    (!call.IsUpdate && providedElsewhere.Contains((call.Source, call.Destination))))
                {
                    continue;
                }

                var key = (call.IsUpdate, call.Source, call.Destination);
                if (!bound.TryGetValue(key, out var sites))
                {
                    bound[key] = sites = new List<DwarfMapCallSite>();
                }

                sites.Add(call);
            }

            if (bound.Count == 0)
            {
                return null;
            }

            var sb = new StringBuilder();
            sb.Append("// <auto-generated/>\n// SPDX-License-Identifier: GPL-2.0-only\n#nullable enable\n\n");
            sb.AppendLine("namespace System.Runtime.CompilerServices");
            sb.AppendLine("{");
            sb.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
            sb.AppendLine("    file sealed class InterceptsLocationAttribute : global::System.Attribute");
            sb.AppendLine("    {");
            sb.AppendLine("        public InterceptsLocationAttribute(int version, string data)");
            sb.AppendLine("        {");
            sb.AppendLine("            _ = version;");
            sb.AppendLine("            _ = data;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.Append("namespace ").Append(InterceptorsNamespace).Append('\n');
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Dwarf.Map calls this assembly registers the pair of, bound at compile time: each runs the");
            sb.AppendLine("    /// expression its registration runs, on the same mapper instance, without the registry lookup.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    internal static partial class __DwarfMapperAmbientRegistration");
            sb.AppendLine("    {");
            var index = 0;
            foreach (var entry in bound)
            {
                var (isUpdate, source, destination) = entry.Key;
                var body = byPair[(source, destination, isUpdate)].Body;
                if (index > 0)
                {
                    sb.AppendLine();
                }

                foreach (var site in entry.Value.OrderBy(static s => s.LocationData, StringComparer.Ordinal))
                    sb.Append("        [global::System.Runtime.CompilerServices.InterceptsLocation(")
                        .Append(site.LocationVersion).Append(", \"").Append(site.LocationData).Append("\")]\n");

                if (isUpdate)
                {
                    // The registry's Update refuses a null source or destination before it looks anything up; the
                    // bound call does the same, in the same order, so the exception is identical.
                    sb.Append("        internal static void __DwarfMap_").Append(index).Append('(').Append(source)
                        .Append(" source, ").Append(destination).Append(" destination)\n");
                    sb.AppendLine("        {");
                    sb.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(source);");
                    sb.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(destination);");
                    sb.Append("            ").Append(body).Append(";\n");
                    sb.AppendLine("        }");
                }
                else
                {
                    sb.Append("        internal static ").Append(destination).Append(" __DwarfMap_").Append(index)
                        .Append('(').Append(source).Append(" source) => ").Append(body).Append(";\n");
                }

                index++;
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>Create maps first, then update-into; each by source then destination, ordinally.</summary>
        private sealed class PairOrder : IComparer<(bool, string, string)>
        {
            public static readonly PairOrder Instance = new();

            public int Compare((bool, string, string) x, (bool, string, string) y)
            {
                var c = x.Item1.CompareTo(y.Item1);
                if (c != 0)
                {
                    return c;
                }

                c = string.CompareOrdinal(x.Item2, y.Item2);
                return c != 0 ? c : string.CompareOrdinal(x.Item3, y.Item3);
            }
        }
    }
}
