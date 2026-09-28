// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        private const string IsClosedTypeAttributeFqn = "System.Runtime.CompilerServices.IsClosedTypeAttribute";

        /// <summary>
        ///     <c>DWARF114</c> — a <c>[MapDerivedType]</c> dispatch over a C# 15 <c>closed</c> source type that leaves a
        ///     direct descendant without an arm.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Round 31 T25, research C2. A <c>closed</c> class may be derived from only inside its own assembly, so its
        ///         direct descendants are a complete, known set — the same fact the C# compiler uses to call a <c>switch</c>
        ///         over them exhaustive. Without this check a missing arm is the dispatch switch's runtime fallback, which
        ///         throws for an instance of the missing type; with it, the gap is a build error. The doctrine applied to
        ///         polymorphism: what the compiler can see is not left for the runtime to find.
        ///     </para>
        ///     <para>
        ///         A descendant is covered when an arm's source type is it or one of its ancestors below the base (an arm
        ///         matches every subtype of its source), or when it is itself closed and every one of ITS direct
        ///         descendants is covered — closedness is not transitive, so that is the only way coverage can descend.
        ///     </para>
        ///     <para>
        ///         <b>Detection is by <c>IsClosedTypeAttribute</c></b>, which the C# 15 compiler emits on every closed class
        ///         (closed-hierarchies spec, "Lowering"). That reads on the current Roslyn floor, so no versioned analyzer
        ///         folder is needed — the packaging question T25 raised does not arise. The one case it does not reach is a
        ///         closed class declared in the SAME compilation as the mapper: a compiler-synthesized attribute is not
        ///         returned by <c>GetAttributes()</c> for a source symbol, and the symbol API that reports <c>closed</c>
        ///         exists only in a Roslyn newer than the floor. That half waits for the floor to move; a closed hierarchy
        ///         from a referenced assembly — a domain model in its own project, the usual shape — is checked today.
        ///         Arms are not INFERRED: pairing each derived source with a derived destination needs a rule nobody has
        ///         stated, and guessing one would be a silent mapping decision.
        ///     </para>
        /// </remarks>
        private static void ReportMissingClosedHierarchyArms(
            ITypeSymbol sourceType,
            IReadOnlyList<INamedTypeSymbol> armSources,
            string methodName,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            if (sourceType is not INamedTypeSymbol closedBase || !IsClosedType(closedBase))
            {
                return;
            }

            var missing = new List<string>();
            foreach (var descendant in DirectDescendants(closedBase))
                if (!IsCovered(descendant, closedBase, armSources))
                {
                    missing.Add(descendant.ToDisplayString());
                }

            if (missing.Count == 0)
            {
                return;
            }

            missing.Sort(StringComparer.Ordinal);
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ClosedHierarchyArmMissing,
                location,
                $"'{methodName}' dispatches over the closed type '{closedBase.ToDisplayString()}', whose direct " +
                $"descendant(s) {string.Join(", ", missing.Select(m => "'" + m + "'"))} have no [MapDerivedType] arm; an " +
                "instance of one would reach the dispatch's run-time fallback and throw. Add an arm for each, or for an " +
                "ancestor that covers it"));
        }

        private static bool IsClosedType(INamedTypeSymbol type)
        {
            foreach (var a in type.GetAttributes())
                if (KnownNames.IsAttributeClass(a.AttributeClass, IsClosedTypeAttributeFqn))
                {
                    return true;
                }

            return false;
        }

        private static bool IsCovered(INamedTypeSymbol type, INamedTypeSymbol closedBase, IReadOnlyList<INamedTypeSymbol> armSources)
        {
            for (var t = type; t is not null && !SymbolEqualityComparer.Default.Equals(t, closedBase); t = t.BaseType)
                if (armSources.Any(s => SymbolEqualityComparer.Default.Equals(s, t)))
                {
                    return true;
                }

            if (!IsClosedType(type))
            {
                return false;
            }

            var children = DirectDescendants(type);
            return children.Count > 0 && children.All(c => IsCovered(c, closedBase, armSources));
        }

        /// <summary>Every type in <paramref name="closedBase" />'s own assembly that derives from it directly.</summary>
        private static List<INamedTypeSymbol> DirectDescendants(INamedTypeSymbol closedBase)
        {
            var found = new List<INamedTypeSymbol>();
            var pending = new Stack<INamespaceOrTypeSymbol>();
            pending.Push(closedBase.ContainingAssembly.GlobalNamespace);
            while (pending.Count > 0)
            {
                var container = pending.Pop();
                foreach (var member in container.GetMembers())
                {
                    if (member is INamespaceSymbol ns)
                    {
                        pending.Push(ns);
                    }
                    else if (member is INamedTypeSymbol type)
                    {
                        pending.Push(type);
                        if (SymbolEqualityComparer.Default.Equals(type.BaseType?.OriginalDefinition, closedBase.OriginalDefinition))
                        {
                            found.Add(type);
                        }
                    }
                }
            }

            return found;
        }
    }
}
