// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        private static List<PairConstructor> ReadPairConstructors(INamedTypeSymbol classSymbol)
        {
            var result = new List<PairConstructor>();
            foreach (var attr in classSymbol.GetAttributes())
            {
                var ac = attr.AttributeClass;
                if (ac is null || ac.Name != KnownNames.MapConstructor || ac.TypeArguments.Length != 2 || !KnownNames.IsNamespace(ac.ContainingNamespace, KnownNames.Ns))
                {
                    continue;
                }

                if (attr.ConstructorArguments.Length != 1)
                {
                    continue;
                }

                // A null factory name names no method, exactly as a name matching none does. It is kept, so the pair
                // reports DWARF059 (or DWARF056 when no pair matches) instead of the directive vanishing silently.
                var method = attr.ConstructorArguments[0].Value as string ?? string.Empty;

                result.Add(new PairConstructor
                {
                    Source = ac.TypeArguments[0],
                    Target = ac.TypeArguments[1],
                    Method = method,
                    Loc = LocationInfo.FromReference(attr.ApplicationSyntaxReference)
                });
            }

            return result;
        }

        /// <summary>
        ///     Names of the destination members the generator must <b>not</b> assign when a factory constructs the
        ///     target (<c>[MapConstructor]</c>): everything except a plain post-construction-settable member. That is,
        ///     <c>init</c>-only, <c>required</c>, and get-only/read-only members — all of which can only be set at
        ///     construction time and are therefore the factory's responsibility. Walks the inheritance chain.
        /// </summary>
        private static HashSet<string> CollectFactoryExcludedMembers(INamedTypeSymbol target)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (ITypeSymbol? t = target; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                foreach (var m in t.GetMembers())
                    if (m is IPropertySymbol p && !p.IsIndexer)
                    {
                        // Keep only plain settable properties; exclude required, get-only, and init-only.
                        if (p.IsRequired || p.SetMethod is null || p.SetMethod.IsInitOnly)
                        {
                            result.Add(p.Name);
                        }
                    }
                    else if (m is IFieldSymbol f && !f.IsImplicitlyDeclared)
                    {
                        if (f.IsRequired || f.IsReadOnly || f.IsConst)
                        {
                            result.Add(f.Name);
                        }
                    }

            return result;
        }

        private static List<PairProp> ReadPairMapProperties(INamedTypeSymbol classSymbol)
        {
            var result = new List<PairProp>();
            foreach (var attr in classSymbol.GetAttributes())
            {
                var ac = attr.AttributeClass;
                if (ac is null || ac.Name != KnownNames.MapProperty || ac.TypeArguments.Length != 2 || !KnownNames.IsNamespace(ac.ContainingNamespace, KnownNames.Ns))
                {
                    continue;
                }

                if (attr.ConstructorArguments.Length != 2)
                {
                    continue;
                }

                // A null member name names no member, exactly as a name matching none does. It is kept, so the pair
                // reports DWARF009 / DWARF008 instead of the directive vanishing silently.
                var src = attr.ConstructorArguments[0].Value as string ?? string.Empty;
                var tgt = attr.ConstructorArguments[1].Value as string ?? string.Empty;

                string? use = null;
                var hasNull = false;
                TypedConstant nullSub = default;
                string? when = null;
                foreach (var na in attr.NamedArguments)
                    if (na.Key == "Use" && na.Value.Value is string u)
                    {
                        use = u;
                    }
                    else if (na.Key == "NullSubstitute")
                    {
                        hasNull = true;
                        nullSub = na.Value;
                    }
                    else if (na.Key == "When" && na.Value.Value is string w)
                    {
                        when = w;
                    }

                result.Add(new PairProp
                {
                    Source = ac.TypeArguments[0],
                    Target = ac.TypeArguments[1],
                    SrcMember = src,
                    TgtMember = tgt,
                    Use = use,
                    HasNullSub = hasNull,
                    NullSub = nullSub,
                    When = when,
                    Loc = LocationInfo.FromReference(attr.ApplicationSyntaxReference)
                });
            }

            return result;
        }

        private static List<PairIgnore> ReadPairIgnores(INamedTypeSymbol classSymbol)
        {
            var result = new List<PairIgnore>();
            foreach (var attr in classSymbol.GetAttributes())
            {
                var ac = attr.AttributeClass;
                if (ac is null || ac.Name != KnownNames.MapIgnore || ac.TypeArguments.Length != 1 || !KnownNames.IsNamespace(ac.ContainingNamespace, KnownNames.Ns))
                {
                    continue;
                }

                if (attr.ConstructorArguments.Length != 1 ||
                    attr.ConstructorArguments[0].Value is not string member)
                {
                    continue;
                }

                result.Add(new PairIgnore
                {
                    Target = ac.TypeArguments[0],
                    Member = member,
                    Loc = LocationInfo.FromReference(attr.ApplicationSyntaxReference)
                });
            }

            return result;
        }

        /// <summary>
        ///     True when a pair-scoped <c>[MapConstructor&lt;S,T&gt;]</c> names exactly <c>(src, tgt)</c>.
        /// </summary>
        private static bool IsPairCtorMatch(PairConstructor pc, ITypeSymbol src, ITypeSymbol tgt)
        {
            return SymbolEqualityComparer.Default.Equals(pc.Source, src) && SymbolEqualityComparer.Default.Equals(pc.Target, tgt);
        }

        /// <summary>
        ///     Non-mutating query form of the <c>[MapConstructor&lt;S,T&gt;]</c> lookup — see
        ///     <see cref="AnyPairProp" />'s remarks for why asking must not mark anything <c>Consumed</c>.
        /// </summary>
        /// <remarks>
        ///     Round 29 T0.2c review fix 1. The blit gate needs this because a factory is baked into whichever
        ///     route the element pair takes, and under Preserve/SetNull that route is the SYNTHESIZED helper
        ///     rather than the declared method — so the "did the user declare a conversion?" question answers no
        ///     and the pair still is not blittable. Callers must scope the question to pairs some
        ///     <c>[GenerateMap&lt;S,T&gt;]</c> declares, which is the same scoping
        ///     <c>DrainNestedMappingQueue</c>'s factory wiring applies: a <c>[MapConstructor]</c> naming an
        ///     undeclared pair is honoured by nobody and reported by DWARF056.
        /// </remarks>
        private static bool AnyPairConstructor(List<PairConstructor> all, ITypeSymbol src, ITypeSymbol tgt)
        {
            foreach (var pc in all)
                if (IsPairCtorMatch(pc, src, tgt))
                {
                    return true;
                }

            return false;
        }

        /// <summary>True when a pair-scoped <c>[MapProperty&lt;S,T&gt;]</c> targets exactly <c>(src, tgt)</c>.</summary>
        private static bool IsPairPropMatch(PairProp p, ITypeSymbol src, ITypeSymbol tgt)
        {
            return SymbolEqualityComparer.Default.Equals(p.Source, src) && SymbolEqualityComparer.Default.Equals(p.Target, tgt);
        }

        /// <summary>True when a pair-scoped <c>[MapIgnore&lt;T&gt;]</c>/<c>[MapValue&lt;T&gt;]</c> targets exactly <c>tgt</c>.</summary>
        private static bool IsPairTargetMatch(ITypeSymbol candidateTarget, ITypeSymbol tgt)
        {
            return SymbolEqualityComparer.Default.Equals(candidateTarget, tgt);
        }

        /// <summary>
        ///     Returns the pair-scoped explicit renames and NullSubstitute/When extras for the <c>(src → tgt)</c> pair,
        ///     marking each matching attribute as consumed (for the DWARF056 "matched nothing" check). Round 29 T0.2
        ///     fix-round-2: callers that only need to ASK whether a pair-scoped rename applies — without deciding
        ///     resolution, and so without owning the right to mark it consumed — use the non-mutating
        ///     <see cref="AnyPairProp" /> instead, which shares this method's <see cref="IsPairPropMatch" /> rule.
        /// </summary>
        private static (List<(string Source, string Target, string? Use)> Explicit,
            List<(string Target, bool HasNullSub, TypedConstant NullSub, string? When, string? NullSubLiteral)> Extras)
            MatchPairProps(List<PairProp> all, ITypeSymbol src, ITypeSymbol tgt)
        {
            var ex = new List<(string Source, string Target, string? Use)>();
            var extras = new List<(string Target, bool HasNullSub, TypedConstant NullSub, string? When, string? NullSubLiteral)>();
            foreach (var p in all)
            {
                if (!IsPairPropMatch(p, src, tgt))
                {
                    continue;
                }

                p.Consumed = true;
                ex.Add((p.SrcMember, p.TgtMember, p.Use));
                if (p.HasNullSub || p.When is not null)
                {
                    extras.Add((p.TgtMember, p.HasNullSub, p.NullSub, p.When, p.NullSubLiteral));
                }
            }

            return (ex, extras);
        }

        /// <summary>
        ///     True when some pair-scoped <c>[MapProperty&lt;S,T&gt;]</c> targets exactly <c>(src, tgt)</c> —
        ///     the QUERY form of <see cref="MatchPairProps" />, sharing its <see cref="IsPairPropMatch" /> rule
        ///     but never marking <c>Consumed</c>. For a caller deciding whether to REFUSE a fast path because a
        ///     directive customizes the pair (round 29 T0.2 fix-round-2): marking it consumed there would be
        ///     wrong when nothing else ever applies it — that pair-scoped attribute would then silently vanish
        ///     from the DWARF056 "matched no pair" sweep instead of correctly flagging a directive nothing reads.
        /// </summary>
        private static bool AnyPairProp(List<PairProp> all, ITypeSymbol src, ITypeSymbol tgt)
        {
            foreach (var p in all)
                if (IsPairPropMatch(p, src, tgt))
                {
                    return true;
                }

            return false;
        }

        private static HashSet<string> MatchPairIgnores(List<PairIgnore> all, ITypeSymbol tgt)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ig in all)
                if (IsPairTargetMatch(ig.Target, tgt))
                {
                    ig.Consumed = true;
                    set.Add(ig.Member);
                }

            return set;
        }

        /// <summary>Non-mutating query form of <see cref="MatchPairIgnores" /> — see <see cref="AnyPairProp" />'s remarks.</summary>
        private static bool AnyPairIgnore(List<PairIgnore> all, ITypeSymbol tgt)
        {
            foreach (var ig in all)
                if (IsPairTargetMatch(ig.Target, tgt))
                {
                    return true;
                }

            return false;
        }

        private static List<PairValue> ReadPairMapValues(INamedTypeSymbol classSymbol)
        {
            var result = new List<PairValue>();
            foreach (var attr in classSymbol.GetAttributes())
            {
                var ac = attr.AttributeClass;
                if (ac is null || ac.Name != KnownNames.MapValue || ac.TypeArguments.Length != 1 || !KnownNames.IsNamespace(ac.ContainingNamespace, KnownNames.Ns))
                {
                    continue;
                }

                if (attr.ConstructorArguments.Length == 0)
                {
                    continue;
                }

                // A null target name names no member, exactly as a name matching none does. It is kept, so the pair
                // reports DWARF042 instead of the directive vanishing silently.
                var target = attr.ConstructorArguments[0].Value as string ?? string.Empty;

                var use = TryGetNamedArgument(attr.NamedArguments, "Use", out var u) ? u.Value as string : null;

                // Two-arg ctor → constant value in [1]; one-arg ctor → Use-driven (mirrors ReadMapValues).
                var isConstant = attr.ConstructorArguments.Length == 2 && use is null;
                var value = attr.ConstructorArguments.Length == 2 ? attr.ConstructorArguments[1] : default;
                result.Add(new PairValue
                {
                    Target = ac.TypeArguments[0],
                    Member = target,
                    IsConstant = isConstant,
                    Value = value,
                    Use = use,
                    Loc = LocationInfo.FromReference(attr.ApplicationSyntaxReference)
                });
            }

            return result;
        }

        private static List<(string Target, bool IsConstant, TypedConstant Value, string? Use, string? ConstLiteral)>
            MatchPairValues(List<PairValue> all, ITypeSymbol tgt)
        {
            var result = new List<(string Target, bool IsConstant, TypedConstant Value, string? Use, string? ConstLiteral)>();
            foreach (var v in all)
                if (IsPairTargetMatch(v.Target, tgt))
                {
                    v.Consumed = true;
                    result.Add((v.Member, v.IsConstant, v.Value, v.Use, v.ConstLiteral));
                }

            return result;
        }

        /// <summary>Non-mutating query form of <see cref="MatchPairValues" /> — see <see cref="AnyPairProp" />'s remarks.</summary>
        private static bool AnyPairValue(List<PairValue> all, ITypeSymbol tgt)
        {
            foreach (var v in all)
                if (IsPairTargetMatch(v.Target, tgt))
                {
                    return true;
                }

            return false;
        }

        // ── Pair-scoped member config: [MapProperty<S,T>] / [MapIgnore<T>] declared on the class ──────────────
        // These give a [GenerateMap] pair (or an auto-synthesized nested pair) member config without a partial
        // method. The non-generic readers above match on the exact display string KnownNames.MapPropertyFqn
        // etc., so they never pick up these generic variants; these readers match by name + type-argument arity.

        private sealed class PairProp
        {
            public bool Consumed;
            public bool HasNullSub;
            public LocationInfo? Loc;
            public TypedConstant NullSub;
            public string? NullSubLiteral; // pre-rendered literal when the null-substitute came from MapConfig
            public ITypeSymbol Source = null!;
            public string SrcMember = "";
            public ITypeSymbol Target = null!;
            public string TgtMember = "";
            public string? Use;
            public string? When;
        }

        private sealed class PairIgnore
        {
            public bool Consumed;
            public LocationInfo? Loc;
            public string Member = "";
            public ITypeSymbol Target = null!;
        }

        private sealed class PairConstructor
        {
            public bool Consumed;
            public LocationInfo? Loc;
            public string Method = "";
            public ITypeSymbol Source = null!;
            public ITypeSymbol Target = null!;
        }

        private sealed class PairValue
        {
            public string? ConstLiteral; // pre-rendered literal when the value came from MapConfig
            public bool Consumed;
            public bool IsConstant;
            public LocationInfo? Loc;
            public string Member = "";
            public ITypeSymbol Target = null!;
            public string? Use;
            public TypedConstant Value;
        }
    }
}
