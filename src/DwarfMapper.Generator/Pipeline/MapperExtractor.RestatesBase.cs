// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     <c>DWARF084</c> / <c>DWARF085</c> — checks each <c>[RestatesBase&lt;S,T&gt;]</c> pair against the base
        ///     pair it restates.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         DwarfMapper deliberately has no <c>IncludeBase</c>: every pair's configuration stays literally
        ///         visible at its own declaration. The cost is restatement, and that cost has two halves — typing
        ///         it, which is mechanical and over once, and drifting from the base later, which is silent and
        ///         only ever drifts toward wrong data. This closes the second half without introducing override
        ///         semantics that would have to interact with pair-scoped attributes, <c>[MapDerivedType]</c> and
        ///         the policy layer. See <c>Issues/Rount18/Decisions.md</c> §D4.
        ///     </para>
        ///     <para>
        ///         The comparison is on RESOLVED mappings, not on attribute text. A restatement that is present but
        ///         no longer does the same thing — the base gained a <c>Use=</c> converter, the derived one kept
        ///         mapping the raw value — is exactly the failure worth catching, and it is invisible to any check
        ///         that counts attributes or reads marker comments.
        ///     </para>
        /// </remarks>
        private static void CheckRestatedBases(
            INamedTypeSymbol classSymbol,
            List<MapMethodModel> methods,
            LocationInfo? classLocation,
            List<DiagnosticInfo> diagnostics)
        {
            var declarations = ReadRestatesBase(classSymbol, classLocation);
            if (declarations.Count == 0)
            {
                return;
            }

            // Every pair this class actually maps at the top level, by (sourceFqn, targetFqn). Nested synthesized
            // helpers are excluded: they are not declarations, so there is nothing for an author to restate.
            var declaredPairs = new List<(ITypeSymbol Src, ITypeSymbol Tgt, MapMethodModel Model)>();
            foreach (var m in methods)
            {
                if (GeneratedNames.IsAnySynthesized(m.MethodName))
                {
                    continue;
                }

                var src = ResolveByFqn(classSymbol, m.ParameterTypeFullName);
                var tgt = ResolveByFqn(classSymbol, m.ReturnTypeFullName);
                if (src is not null && tgt is not null)
                {
                    declaredPairs.Add((src, tgt, m));
                }
            }

            foreach (var (derivedSrc, derivedTgt, overrides, location) in declarations)
            {
                var derived = declaredPairs.Find(p =>
                    SymbolEqualityComparer.Default.Equals(p.Src, derivedSrc) && SymbolEqualityComparer.Default.Equals(p.Tgt, derivedTgt));

                if (derived.Model is null)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.RestatesBaseUnresolved,
                        location,
                        $"[RestatesBase<{derivedSrc.ToDisplayString()}, {derivedTgt.ToDisplayString()}>] names a " + "pair this mapper does not declare. Add the pair it is about " + $"([GenerateMap<{derivedSrc.ToDisplayString()}, {derivedTgt.ToDisplayString()}>] or a " + "partial method for it), or fix the type arguments."));
                    continue;
                }

                if (!TryFindBasePair(declaredPairs, derivedSrc, derivedTgt, out var basePair, out var ambiguous))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.RestatesBaseUnresolved,
                        location,
                        ambiguous is null
                            ? $"[RestatesBase<{derivedSrc.ToDisplayString()}, {derivedTgt.ToDisplayString()}>] " +
                              "finds no base pair to restate on this mapper. A base pair is one whose SOURCE is a " +
                              $"base class of '{derivedSrc.ToDisplayString()}' and whose TARGET is a base class of " +
                              $"'{derivedTgt.ToDisplayString()}', declared on this same mapper. Declare it, or " +
                              "remove the attribute — there is nothing for it to check."
                            : $"[RestatesBase<{derivedSrc.ToDisplayString()}, {derivedTgt.ToDisplayString()}>] finds " + $"more than one equally-close base pair ({ambiguous}). Only base CLASSES are walked, " + "so this means two candidates sit at the same depth; declare the pair you mean and " + "remove the other, or drop the attribute."));
                    continue;
                }

                ReportDrift(derived.Model, basePair.Model, derivedTgt, basePair.Tgt, overrides, location, diagnostics);
            }
        }

        /// <summary>
        ///     The most-derived declared pair strictly above <paramref name="derivedSrc" />/<paramref name="derivedTgt" />.
        /// </summary>
        /// <remarks>
        ///     Only base CLASSES are walked, deliberately. An interface base has no single distance, so "the
        ///     nearest one" would be a coin toss dressed up as a rule — and a coin toss deciding which
        ///     configuration a drift check compares against is worse than refusing.
        /// </remarks>
        private static bool TryFindBasePair(
            List<(ITypeSymbol Src, ITypeSymbol Tgt, MapMethodModel Model)> declaredPairs,
            ITypeSymbol derivedSrc,
            ITypeSymbol derivedTgt,
            out (ITypeSymbol Src, ITypeSymbol Tgt, MapMethodModel Model) basePair,
            out string? ambiguous)
        {
            basePair = default;
            ambiguous = null;

            var best = -1;
            var tied = new List<string>();

            foreach (var candidate in declaredPairs)
            {
                var srcDepth = BaseDepth(derivedSrc, candidate.Src);
                var tgtDepth = BaseDepth(derivedTgt, candidate.Tgt);
                if (srcDepth <= 0 || tgtDepth <= 0)
                {
                    continue; // not a STRICT base on both sides
                }

                var depth = srcDepth + tgtDepth;
                if (best < 0 || depth < best)
                {
                    best = depth;
                    basePair = candidate;
                    tied.Clear();
                    tied.Add(candidate.Src.ToDisplayString() + " -> " + candidate.Tgt.ToDisplayString());
                }
                else if (depth == best)
                {
                    tied.Add(candidate.Src.ToDisplayString() + " -> " + candidate.Tgt.ToDisplayString());
                }
            }

            if (best < 0)
            {
                return false;
            }

            if (tied.Count > 1)
            {
                ambiguous = string.Join(", ", tied);
                return false;
            }

            return true;
        }

        /// <summary>Steps from <paramref name="derived" /> up to <paramref name="candidate" />; 0 if not a strict base.</summary>
        private static int BaseDepth(ITypeSymbol derived, ITypeSymbol candidate)
        {
            var depth = 0;
            for (var t = derived.BaseType; t is not null; t = t.BaseType)
            {
                depth++;
                if (SymbolEqualityComparer.Default.Equals(t, candidate))
                {
                    return depth;
                }
            }

            return 0;
        }

        private static void ReportDrift(
            MapMethodModel derived,
            MapMethodModel baseModel,
            ITypeSymbol derivedTgt,
            ITypeSymbol baseTgt,
            IReadOnlyCollection<string> overrides,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            var drifted = new List<string>();
            var dropped = new List<string>();

            foreach (var baseMember in baseModel.Members)
            {
                if (overrides.Contains(baseMember.TargetName, StringComparer.Ordinal))
                {
                    continue;
                }

                // A member the derived target REDECLARES (a `new` member of a different type) is a different
                // member wearing the same name; comparing their mappings would be comparing two unrelated things.
                if (!SameMemberType(baseTgt, derivedTgt, baseMember.TargetName))
                {
                    continue;
                }

                var derivedMember = derived.Members.FirstOrDefault(m =>
                    string.Equals(m.TargetName, baseMember.TargetName, StringComparison.Ordinal));

                if (derivedMember is null)
                {
                    // Mapped in the base, unmapped here. Usually an [MapIgnore] the base does not have — the
                    // shape that quietly stops carrying a value.
                    dropped.Add(baseMember.TargetName);
                    continue;
                }

                // Record equality catches everything the mapping actually does: the converter, the null
                // substitute, the When predicate, the source path. Comparing attribute text would not.
                if (derivedMember != baseMember)
                {
                    drifted.Add(baseMember.TargetName);
                }
            }

            if (drifted.Count == 0 && dropped.Count == 0)
            {
                return;
            }

            var parts = new List<string>();
            if (drifted.Count > 0)
            {
                parts.Add("maps " + Join(drifted) + " differently from the base pair");
            }

            if (dropped.Count > 0)
            {
                parts.Add("does not map " + Join(dropped) + ", which the base pair maps");
            }

            var first = drifted.Count > 0 ? drifted[0] : dropped[0];

            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.RestatedBaseDrift,
                location,
                $"'{derived.ParameterTypeFullName}' -> '{derived.ReturnTypeFullName}' declares [RestatesBase] but " +
                string.Join(", and it ", parts) +
                $". The base pair is '{baseModel.ParameterTypeFullName}' -> '{baseModel.ReturnTypeFullName}'. " +
                "Restate the base configuration here, or — if the difference is intended — say so with " +
                "[RestatesBase(Overrides = new[] { \"" +
                first +
                "\" })], which exempts that member and leaves every other one guarded.",
                MemberName: first,
                // Handed to the code fix so it copies from the pair the GENERATOR chose, rather than re-deriving
                // the base-pair rule in a second place that is free to disagree.
                SourcePair: baseModel.ParameterTypeFullName + "|" + baseModel.ReturnTypeFullName));
        }

        private static string Join(List<string> names)
        {
            return names.Count <= 3
                ? string.Join(", ", names)
                : string.Join(", ", names.Take(3)) + $" (and {names.Count - 3} more)";
        }

        /// <summary>
        ///     Whether both targets expose a member of that name with the SAME type.
        /// </summary>
        /// <remarks>
        ///     False for a <c>new</c>-hidden member whose type changed — two different members that happen to share
        ///     a name. Their mappings are supposed to differ, and reporting that would be the check crying wolf on
        ///     the one shape where divergence is the whole point.
        /// </remarks>
        internal static bool SameMemberType(ITypeSymbol baseTgt, ITypeSymbol derivedTgt, string memberName)
        {
            var baseType = MemberTypeOf(baseTgt, memberName);
            var derivedType = MemberTypeOf(derivedTgt, memberName);

            return baseType is not null && derivedType is not null && SymbolEqualityComparer.Default.Equals(baseType, derivedType);
        }

        private static ITypeSymbol? MemberTypeOf(ITypeSymbol type, string memberName)
        {
            for (var t = type; t is not null; t = t.BaseType)
                foreach (var m in t.GetMembers(memberName))
                    switch (m)
                    {
                        case IPropertySymbol p: return p.Type;

                        case IFieldSymbol f: return f.Type;
                    }

            return null;
        }

        /// <summary>Resolves a <c>global::</c>-rooted display name back to the symbol, within this compilation.</summary>
        private static ITypeSymbol? ResolveByFqn(INamedTypeSymbol classSymbol, string fqn)
        {
            // The models carry display strings rather than symbols (they must stay value-equatable), so the check
            // has to get back to symbols to ask about base chains and member types. Matching against the pairs the
            // class itself declares keeps this to a small, local search rather than a compilation-wide lookup.
            foreach (var (_, ac) in WithNonNullKey(classSymbol.GetAttributes(), a => a.AttributeClass))
            {
                if (ac.TypeArguments.Length != 2)
                {
                    continue;
                }

                foreach (var candidate in ac.TypeArguments)
                    if (string.Equals(candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            fqn,
                            StringComparison.Ordinal))
                    {
                        return candidate;
                    }
            }

            foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                if (m.MethodKind != MethodKind.Ordinary || m.Parameters.Length != 1)
                {
                    continue;
                }

                if (string.Equals(m.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        fqn,
                        StringComparison.Ordinal))
                {
                    return m.Parameters[0].Type;
                }

                if (string.Equals(m.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        fqn,
                        StringComparison.Ordinal))
                {
                    return m.ReturnType;
                }
            }

            return null;
        }

        private static List<(ITypeSymbol Src, ITypeSymbol Tgt, HashSet<string> Overrides, LocationInfo? Loc)>
            ReadRestatesBase(INamedTypeSymbol classSymbol, LocationInfo? classLocation)
        {
            var result = new List<(ITypeSymbol, ITypeSymbol, HashSet<string>, LocationInfo?)>();

            foreach (var attr in classSymbol.GetAttributes())
            {
                if (attr.AttributeClass is not { Name: KnownNames.RestatesBase } ac || ac.TypeArguments.Length != 2 || !KnownNames.IsNamespace(ac.ContainingNamespace, KnownNames.Ns))
                {
                    continue;
                }

                var overrides = new HashSet<string>(StringComparer.Ordinal);
                if (TryGetNamedArgument(attr.NamedArguments, "Overrides", out var named))
                {
                    foreach (var v in named.Values)
                        if (v.Value is string s && s.Length > 0)
                        {
                            overrides.Add(s);
                        }
                }

                // Anchored at the attribute, or at the mapper class when the attribute has no position of its own.
                var loc = LocationInfo.FromReference(attr.ApplicationSyntaxReference, classLocation);

                result.Add((ac.TypeArguments[0], ac.TypeArguments[1], overrides, loc));
            }

            return result;
        }
    }
}
