// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Text;
using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        private static List<string> ReadReinterpretMembers(ISymbol method)
        {
            var members = new List<string>();
            foreach (var attr in method.GetAttributes())
                if (KnownNames.IsAttributeClass(attr.AttributeClass, KnownNames.ReinterpretFqn) && attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is string m)
                {
                    members.Add(m);
                }

            return members;
        }

        /// <summary>
        ///     Every <c>[MapShare("Member")]</c> on a mapping method, as the caller wrote it.
        /// </summary>
        /// <remarks>
        ///     The names come back RAW — never escaped. They are COMPARED against destination member names
        ///     (<c>TryPlanShare</c>'s <c>ShareMembers.Contains</c>) and printed into <c>DWARF104</c>; escaping is
        ///     positional, and an <c>@</c> that leaks into a comparison is how a diagnostic came to refuse a
        ///     member that was plainly mapped. Emission is the only place the <c>@</c> belongs, and the share
        ///     emits the DESTINATION member's own escaped name via <c>MemberMap.EmitTargetName</c>, never this
        ///     string.
        /// </remarks>
        private static List<string> ReadShareMembers(ISymbol method)
        {
            var members = new List<string>();
            foreach (var attr in method.GetAttributes())
                if (KnownNames.IsAttributeClass(attr.AttributeClass, KnownNames.MapShareFqn) &&
                    attr.ConstructorArguments.Length == 1 &&
                    attr.ConstructorArguments[0].Value is string m)
                {
                    members.Add(m);
                }

            return members;
        }

        /// <summary>
        ///     Every <c>[MapDenseEnumKeys("Member", Offset = n)]</c> on a mapping method, as the caller wrote it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Applications are returned IN ORDER and un-deduplicated, so the caller can see that two of them
        ///         named the same member. That is a real mistake with two different offsets behind it — picking
        ///         one silently would emit arithmetic the consumer never asked for — and it is reported as
        ///         <c>DWARF105</c> rather than resolved here.
        ///     </para>
        ///     <para>
        ///         The names come back RAW, never escaped, for the reason <c>ReadShareMembers</c> states: they are
        ///         COMPARED against destination member names and PRINTED into a diagnostic, and escaping is
        ///         positional. The emitted assignment uses the destination member's own escaped name via
        ///         <c>MemberMap.EmitTargetName</c>, and the emitted loop names no consumer identifier at all —
        ///         it reads <c>__kv.Key</c> and <c>__kv.Value</c> off a KeyValuePair.
        ///     </para>
        /// </remarks>
        private static List<(string Member, int Offset)> ReadDenseEnumKeys(ISymbol method)
        {
            var members = new List<(string, int)>();
            foreach (var attr in method.GetAttributes())
            {
                if (!KnownNames.IsAttributeClass(attr.AttributeClass, KnownNames.MapDenseEnumKeysFqn) ||
                    attr.ConstructorArguments.Length != 1 ||
                    !(attr.ConstructorArguments[0].Value is string m))
                {
                    continue;
                }

                var offset = 0;
                foreach (var named in attr.NamedArguments)
                    if (named.Key == "Offset" && named.Value.Value is int o)
                    {
                        offset = o;
                    }

                members.Add((m, offset));
            }

            return members;
        }

        /// <summary>
        ///     Every well-formed <c>[MapCollectionKey("Collection", "Key")]</c> on a mapping method, as written.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Hoisted out of <c>ApplyCollectionKeyUpserts</c>, which was the only reader until the endpoints
        ///         that DISCARD this directive had to name it back to the caller (finding <c>D14</c>). A second
        ///         parse beside a first is the shape that has shipped two generator crashes on this branch, and it
        ///         is also how a message comes to quote an application real resolution never saw: the pair of
        ///         string arguments is the one condition under which the directive exists at all, and both the
        ///         apply path and the refusal path must agree on it exactly.
        ///     </para>
        ///     <para>
        ///         An application whose arguments are absent, <c>null</c>, or not both strings yields nothing —
        ///         so it reaches neither the model nor a diagnostic message, rather than reaching one as the word
        ///         <c>null</c>.
        ///     </para>
        /// </remarks>
        private static List<(string Collection, string Key)> ReadCollectionKeys(ISymbol method)
        {
            var keys = new List<(string, string)>();
            foreach (var attr in method.GetAttributes())
                if (KnownNames.IsAttributeClass(attr.AttributeClass, KnownNames.MapCollectionKeyFqn) && attr.ConstructorArguments.Length >= 2 && attr.ConstructorArguments[0].Value is string collection && attr.ConstructorArguments[1].Value is string key)
                {
                    keys.Add((collection, key));
                }

            return keys;
        }

        private static EnumStrategy ReadEnumStrategy(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "EnumStrategy" && named.Value.Value is int i)
                {
                    return (EnumStrategy)i;
                }

            return EnumStrategy.ByName;
        }

        /// <summary>
        ///     Reads <c>EnumStringSource</c>, the enum↔string half of the enum policy.
        /// </summary>
        /// <remarks>
        ///     Defaults to <c>Attribute</c>: <c>[EnumMember]</c>/<c>[Description]</c> redirecting the serialized
        ///     text is a deliberate feature, and changing that default would silently rewrite the persisted form
        ///     for every existing consumer — the precise failure the option exists to let a migrating consumer
        ///     avoid. <c>DWARF083</c> surfaces the choice; this makes it a one-liner instead of a converter per
        ///     enum.
        /// </remarks>
        private static EnumStringSource ReadEnumStringSource(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "EnumStringSource" && named.Value.Value is int i)
                {
                    return (EnumStringSource)i;
                }

            return EnumStringSource.Attribute;
        }

        private static NullStrategy ReadNullStrategy(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "NullStrategy" && named.Value.Value is int i)
                {
                    return (NullStrategy)i;
                }

            return NullStrategy.Throw;
        }

        private static bool ReadCaseInsensitive(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "CaseInsensitive" && named.Value.Value is bool b)
                {
                    return b;
                }

            return false;
        }

        /// <summary>
        ///     Reads the class-level <c>DwarfMapperAttribute.RegisterCollectionShapes</c>
        ///     value. Defaults to <c>true</c> — the collection registrations are an opt-OUT, because the trap they
        ///     close is invisible to every compile-time check and only fires at first use.
        /// </summary>
        private static bool ReadRegisterCollectionShapes(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "RegisterCollectionShapes" && named.Value.Value is bool b)
                {
                    return b;
                }

            return true;
        }

        /// <summary>
        ///     Reads the class-level <c>DwarfMapperAttribute.GenerateExtensions</c> value
        ///     from the <c>[DwarfMapper]</c> attribute. Defaults to <c>true</c> (the convenience facade is opt-out).
        /// </summary>
        private static bool ReadGenerateExtensions(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "GenerateExtensions" && named.Value.Value is bool b)
                {
                    return b;
                }

            return true;
        }

        /// <summary>
        ///     True when <paramref name="t" /> and every type that contains it are declared <c>public</c> — i.e. it is
        ///     reachable from another assembly. Used to gate <c>public</c> facade extensions (a public extension over a
        ///     non-public type is CS0051) and ambient-registry registration (a cross-assembly map must name both types).
        ///     It inspects the type and its containing-type chain, unwraps arrays to their element type, and recurses
        ///     into generic type arguments — so e.g. <c>ICollection&lt;Internal&gt;</c> / <c>Internal[]</c> are NOT
        ///     effectively public, while <c>ICollection&lt;PublicDto&gt;</c> is.
        /// </summary>
        internal static bool IsEffectivelyPublic(ITypeSymbol t)
        {
            if (t is IArrayTypeSymbol arr)
            {
                return IsEffectivelyPublic(arr.ElementType);
            }

            // The type itself, then each type that contains it. This walked ContainingSymbol up to the namespace,
            // which also carried a null exit no symbol can take — every symbol in that chain is public inside a
            // namespace or fails the accessibility check first — so one branch was untestable by construction.
            // Containing TYPES are the only symbols whose accessibility decides the question, and the walk over them
            // ends on null for every top-level type.
            for (ITypeSymbol? s = t; s is not null; s = s.ContainingType)
                if (s.DeclaredAccessibility != Accessibility.Public)
                {
                    return false;
                }

            if (t is INamedTypeSymbol named)
            {
                foreach (var typeArgument in named.TypeArguments)
                    if (!IsEffectivelyPublic(typeArgument))
                    {
                        return false;
                    }
            }

            return true;
        }

        /// <summary>
        ///     Reads <c>[DwarfMapper(MaxDepth = N)]</c>; defaults to 64; clamps to [DwarfLimits.MinMaxDepth, DwarfLimits.AbsoluteMaxDepth].
        /// </summary>
        private static int ReadMaxDepth(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "MaxDepth" && named.Value.Value is int i)
                {
                    // Clamp to [MinMaxDepth, AbsoluteMaxDepth]. The constants are LINKED from
                    // src/Shared/DwarfLimits.cs into both this assembly and the runtime, so this clamp and
                    // DwarfRefContext's cannot drift apart -- see that file for why it is a shared source
                    // file rather than a test asserting two literals agree.
                    if (i < DwarfLimits.MinMaxDepth)
                    {
                        return DwarfLimits.MinMaxDepth;
                    }

                    if (i > DwarfLimits.AbsoluteMaxDepth)
                    {
                        return DwarfLimits.AbsoluteMaxDepth;
                    }

                    return i;
                }

            return DwarfLimits.DefaultMaxDepth;
        }

        /// <summary>
        ///     Reads the class-level <c>DwarfMapperAttribute.AutoNest</c> value
        ///     from the <c>[DwarfMapper]</c> attribute. Defaults to <c>true</c>.
        /// </summary>
        private static bool ReadAutoNest(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "AutoNest" && named.Value.Value is bool b)
                {
                    return b;
                }

            return true; // default: auto-nesting enabled
        }

        /// <summary>
        ///     Reads the class-level <c>DwarfMapperAttribute.AutoMatchMembers</c> value.
        ///     Defaults to <c>true</c>. When <c>false</c> the mapper is explicit-only (the trust-boundary guard) and
        ///     nothing is auto-wired by name — see <see cref="DiagnosticDescriptors.AutoMatchDisabled" />.
        /// </summary>
        /// <remarks>
        ///     <c>internal</c>, unlike its siblings, because the <c>[MapTo]</c> registry front door enforces the
        ///     same trust boundary and must ask the question with THIS reader rather than one of its own. The
        ///     boundary is the reason: an assembly that switched auto-matching off but had it silently re-enabled at
        ///     one front door has half a guard, and a developer who believes they have one is worse off than a
        ///     developer who knows they do not. The registry passes
        ///     <see cref="AssemblyConfiguration.OptionsFor" />, since it has no mapper class of its own.
        /// </remarks>
        internal static bool ReadAutoMatchMembers(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "AutoMatchMembers" && named.Value.Value is bool b)
                {
                    return b;
                }

            return true; // default: by-name auto-matching enabled
        }

        /// <summary>
        ///     Reads the class-level <c>DwarfMapperAttribute.IgnoreObsoleteMembers</c> value.
        ///     Defaults to <c>false</c>.
        /// </summary>
        private static bool ReadIgnoreObsoleteMembers(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "IgnoreObsoleteMembers" && named.Value.Value is bool b)
                {
                    return b;
                }

            return false;
        }

        /// <summary>
        ///     Names of the accessible instance properties/fields of <paramref name="type" /> that carry
        ///     <c>[System.ObsoleteAttribute]</c> — the members <c>IgnoreObsoleteMembers</c> drops from mapping.
        ///     Walks the inheritance chain so an obsolete member declared on a base type is included too.
        /// </summary>
        private static IEnumerable<string> ObsoleteMemberNames(ITypeSymbol type)
        {
            for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                foreach (var member in t.GetMembers())
                    if (member is IPropertySymbol or IFieldSymbol && IsObsolete(member))
                    {
                        yield return member.Name;
                    }
        }

        private static bool IsObsolete(ISymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
                if (attribute.AttributeClass is { Name: "ObsoleteAttribute" } a && KnownNames.IsNamespace(a.ContainingNamespace, "System"))
                {
                    return true;
                }

            return false;
        }

        /// <summary>
        ///     Reads the class-level <c>DwarfMapperAttribute.SkipNullSourceMembers</c> value.
        ///     Defaults to <c>false</c>.
        /// </summary>
        private static bool ReadSkipNullSourceMembers(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "SkipNullSourceMembers" && named.Value.Value is bool b)
                {
                    return b;
                }

            return false;
        }

        /// <summary>
        ///     Reads a method-level <c>[MapNullSkip]</c> / <c>[MapNullSkip(false)]</c>, or <see langword="null" />
        ///     when the method does not carry one and should inherit the mapper's setting.
        /// </summary>
        /// <remarks>
        ///     Three-state on purpose. A plain <c>bool</c> could not express "inherit", and an attribute argument
        ///     cannot be <c>bool?</c> — which is exactly why this is a separate attribute rather than a named
        ///     property on <c>[GenerateMap]</c>.
        /// </remarks>
        private static bool? ReadMapNullSkip(ISymbol symbol)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                var ac = attr.AttributeClass;
                if (ac is null || ac.Name != KnownNames.MapNullSkip || ac.TypeArguments.Length != 0 || !KnownNames.IsNamespace(ac.ContainingNamespace, KnownNames.Ns))
                {
                    continue;
                }

                // Parameterless usage means "enabled" — the constructor's default.
                return IsEnabledFlag(attr.ConstructorArguments);
            }

            return null;
        }

        /// <summary>
        ///     Reads the pair-scoped <c>[MapNullSkip&lt;TSource, TTarget&gt;]</c> declarations on a mapper class,
        ///     keyed by the pair they configure.
        /// </summary>
        private static List<(ITypeSymbol Source, ITypeSymbol Target, bool Enabled)> ReadPairNullSkips(
            INamedTypeSymbol classSymbol,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            var result = new List<(ITypeSymbol, ITypeSymbol, bool)>();

            foreach (var attr in classSymbol.GetAttributes())
            {
                var ac = attr.AttributeClass;
                if (ac is null || ac.Name != KnownNames.MapNullSkip || ac.TypeArguments.Length != 2 || !KnownNames.IsNamespace(ac.ContainingNamespace, KnownNames.Ns))
                {
                    continue;
                }

                var enabled = IsEnabledFlag(attr.ConstructorArguments);

                // B24 / DWARF099. The set is built here, so this is where a CONTRADICTION over one pair is
                // visible: the resolver below returns the first match by declaration order, which made SOURCE
                // ORDER decide whether a patch-merge mapper skips nulls. Checked against the entries already
                // recorded and only where they DISAGREE — an identical duplicate discards nothing, so it stays
                // accepted in silence. Reported before this entry is added, so the message can name the value
                // it contradicts.
                foreach (var (s, tg, already) in result)
                    if (SymbolEqualityComparer.Default.Equals(s, ac.TypeArguments[0]) && SymbolEqualityComparer.Default.Equals(tg, ac.TypeArguments[1]) && already != enabled)
                    {
                        var src = ac.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                        var tgt = ac.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.ContradictingPairNullSkip,
                            location,
                            $"Mapper '{classSymbol.Name}' declares [MapNullSkip<{src}, {tgt}>({BoolLiteral(already)})] " +
                            $"and [MapNullSkip<{src}, {tgt}>({BoolLiteral(enabled)})] over the SAME pair. The two " +
                            "have identical scope, so only source order separates them and the second was " +
                            "silently discarded. Delete one. To vary the policy per method, use the " +
                            "method-scoped [MapNullSkip(bool)], which wins over this form by design."));
                        break;
                    }

                result.Add((ac.TypeArguments[0], ac.TypeArguments[1], enabled));
            }

            return result;
        }

        /// <summary>
        ///     The <c>enabled</c> flag of a <c>(bool enabled = true)</c> attribute: no argument means the constructor's
        ///     default of <see langword="true" />, a bool means itself, and anything else falls back to the default.
        /// </summary>
        /// <remarks>
        ///     Extracted from ReadMapNullSkip and ReadPairNullSkips and tested directly because the "not a bool" answer
        ///     cannot come from source: a constructor argument that does not bind — wrong type or wrong arity — reaches
        ///     the generator as NO argument, so a non-bool constant in a bool parameter never arrives.
        /// </remarks>
        internal static bool IsEnabledFlag(ImmutableArray<TypedConstant> constructorArguments)
        {
            return constructorArguments.Length == 0 || constructorArguments[0].Value is not bool b || b;
        }

        /// <summary>
        ///     The value of an attribute's single bool constructor argument, when it has exactly one and it is a bool.
        ///     Extracted from ReadMethodAutoNest for the reason <see cref="IsEnabledFlag" /> states.
        /// </summary>
        internal static bool TryReadSingleBool(ImmutableArray<TypedConstant> constructorArguments, out bool value)
        {
            if (constructorArguments.Length == 1 && constructorArguments[0].Value is bool b)
            {
                value = b;
                return true;
            }

            value = false;
            return false;
        }

        /// <summary>The C# literal for a bool, spelled rather than lower-cased at runtime (CA1308).</summary>
        private static string BoolLiteral(bool value)
        {
            return value ? "true" : "false";
        }

        /// <summary>
        ///     The effective null-skip setting for one mapping, from the ONE place every endpoint asks.
        ///     <para>
        ///         Three readers of this option used to exist and each saw a different part of it. The method
        ///         endpoints read <c>ReadMapNullSkip(method) ?? classDefault</c> and never consulted the
        ///         pair-scoped form; the <c>[GenerateMap]</c> and auto-synthesized pairs consulted the pair-scoped
        ///         form and had no method to read; and the projection resolver was handed the bare class value, so
        ///         it saw neither. The two attribute forms are documented as one option written at two scopes, and
        ///         between them a caller reached every endpoint while either alone reached about half — silently.
        ///         Folding all three into this function is why there is no longer a scope that reaches "most" of
        ///         the endpoints.
        ///     </para>
        ///     <para>
        ///         <b>Precedence is most-specific-wins:</b> the method-scoped <c>[MapNullSkip]</c>, then the
        ///         pair-scoped <c>[MapNullSkip&lt;S,T&gt;]</c>, then the mapper/assembly policy in
        ///         <paramref name="classDefault" />. Contradictory forms on one class became reachable the moment
        ///         both fed one resolution, and this is the answer both attributes' own documentation already
        ///         implies: the method form exists to "carve one method out of a class that enables it", which only
        ///         works if it outranks what it is carving out of.
        ///     </para>
        /// </summary>
        /// <param name="pairNullSkips">Every <c>[MapNullSkip&lt;S,T&gt;]</c> on the mapper class.</param>
        /// <param name="method">
        ///     The partial mapping method this mapping is declared by, or <see langword="null" /> for a pair the
        ///     class declared — a <c>[GenerateMap]</c> pair or an auto-synthesized nested/element pair, neither of
        ///     which has a method whose annotations could speak for it.
        /// </param>
        /// <param name="source">The mapping's source type, for matching the pair-scoped form.</param>
        /// <param name="target">The mapping's target type, for matching the pair-scoped form.</param>
        /// <param name="classDefault">The mapper/assembly <c>SkipNullSourceMembers</c> policy.</param>
        private static bool ResolveNullSkip(
            IReadOnlyList<(ITypeSymbol Source, ITypeSymbol Target, bool Enabled)> pairNullSkips,
            ISymbol? method,
            ITypeSymbol source,
            ITypeSymbol target,
            bool classDefault)
        {
            if (method is not null && ReadMapNullSkip(method) is { } methodScoped)
            {
                return methodScoped;
            }

            // First match wins among the entries that reach here, and they can no longer DISAGREE: a pair named
            // twice with opposite values is refused as DWARF099 where the set is built (B24), because the two
            // declarations have identical scope and only source order separated them. Identical duplicates still
            // arrive, and first-match is the right answer for them — they say the same thing.
            foreach (var (s, t, enabled) in pairNullSkips)
                if (SymbolEqualityComparer.Default.Equals(s, source) && SymbolEqualityComparer.Default.Equals(t, target))
                {
                    return enabled;
                }

            return classDefault;
        }

        private static bool ReadAllowNonPublic(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "AllowNonPublic" && named.Value.Value is bool b)
                {
                    return b;
                }

            return false;
        }

        /// <summary>
        ///     Reads <c>[DwarfMapper(NullCollections = ...)]</c>; defaults to <c>AsEmpty</c>.
        /// </summary>
        private static NullCollectionsBehavior ReadNullCollections(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "NullCollections" && named.Value.Value is int i)
                {
                    return (NullCollectionsBehavior)i;
                }

            return NullCollectionsBehavior.AsEmpty;
        }

        /// <summary>
        ///     Reads <c>[DwarfMapper(ReferenceHandling = ...)]</c>; returns the integer value of the
        ///     <c>DwarfMapper.ReferenceHandlingStrategy</c> enum (0 = None, 1 = Preserve).
        ///     Defaults to 0 (None).
        /// </summary>
        private static int ReadReferenceHandling(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "ReferenceHandling" && named.Value.Value is int i)
                {
                    return i;
                }

            return 0; // None
        }

        /// <summary>
        ///     Reads <c>[DwarfMapper(OnCycle = ...)]</c>; returns the integer value of the
        ///     <c>DwarfMapper.OnCycleStrategy</c> enum (0 = Throw, 1 = SetNull).
        ///     Defaults to 0 (Throw).
        /// </summary>
        private static int ReadOnCycle(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "OnCycle" && named.Value.Value is int i)
                {
                    return i;
                }

            return 0; // Throw
        }

        /// <summary>Reads <c>[DwarfMapper(ImplicitConversions = ...)]</c>; defaults to <c>true</c> (permissive).</summary>
        private static bool ReadImplicitConversions(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "ImplicitConversions" && named.Value.Value is bool b)
                {
                    return b;
                }

            return true;
        }

        /// <summary>
        ///     Reads <c>[DwarfMapper(RequiredMapping = ...)]</c>. Returns the enum's int value:
        ///     0 = <c>Target</c> (default — destination-coverage only), 1 = <c>Both</c> (also require every
        ///     source member consumed → DWARF039 for leftovers).
        /// </summary>
        private static int ReadRequiredMapping(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "RequiredMapping" && named.Value.Value is int v)
                {
                    return v;
                }

            return 0; // RequiredMappingStrategy.Target
        }

        /// <summary>Reads <c>[DwarfMapper(NameConvention = ...)]</c>: 0 = Exact (default), 1 = Flexible.</summary>
        private static int ReadNameConvention(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "NameConvention" && named.Value.Value is int v)
                {
                    return v;
                }

            return 0; // NameConvention.Exact
        }

        /// <summary>
        ///     Canonical form for <c>NameConvention.Flexible</c> matching: removes <c>_</c> and lowercases,
        ///     so <c>PascalCase</c>/<c>camelCase</c>/<c>snake_case</c>/<c>UPPER_CASE</c> all reduce to the same key.
        /// </summary>
        private static string NormalizeName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                if (c != '_')
                {
                    sb.Append(char.ToLowerInvariant(c));
                }

            return sb.ToString();
        }

        /// <summary>
        ///     Adds the top-level source member consumed by <paramref name="sourceName" /> to
        ///     <paramref name="consumed" />. A dotted path (a flattened leaf like <c>"Address.City"</c>) marks its
        ///     root (<c>Address</c>) consumed; the empty sentinel (top-level collection / constant value) is ignored.
        /// </summary>
        private static void AddConsumed(HashSet<string> consumed, string sourceName)
        {
            if (string.IsNullOrEmpty(sourceName))
            {
                return;
            }

            var dot = sourceName.IndexOf('.');
            consumed.Add(dot < 0 ? sourceName : sourceName.Substring(0, dot));
        }

        /// <summary>
        ///     Reads the per-method <c>[AutoNest(bool)]</c> attribute override, falling back to
        ///     <paramref name="classDefault" /> when the attribute is absent.
        /// </summary>
        private static bool ReadMethodAutoNest(IMethodSymbol method, bool classDefault)
        {
            foreach (var attr in method.GetAttributes())
                if (KnownNames.IsAttributeClass(attr.AttributeClass, KnownNames.AutoNestFqn) && TryReadSingleBool(attr.ConstructorArguments, out var b))
                {
                    return b;
                }

            return classDefault;
        }
    }
}
