// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     True when the source is a concrete, NON-SEALED class that at least one other type in
        ///     this compilation derives from — i.e. a member declared as this type can hold a subclass instance at
        ///     run time, whose extra members an auto-nested map would silently drop (DWARF071).
        ///     <para>
        ///         Deliberately narrow, to keep the Info actionable rather than ambient noise:
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             <description>sealed source → the declared type IS the runtime type; nothing can be dropped;</description>
        ///         </item>
        ///         <item>
        ///             <description>abstract source → already a hard error, DWARF033; not this diagnostic's business;</description>
        ///         </item>
        ///         <item>
        ///             <description>
        ///                 no derived type anywhere in the compilation → the risk is theoretical. A type
        ///                 derived in a DOWNSTREAM assembly is not visible here, and warning on every non-sealed class on
        ///                 that basis would fire on essentially every DTO in existence.
        ///             </description>
        ///         </item>
        ///     </list>
        /// </summary>
        /// <summary>
        ///     Every type that is a base of some other type declared in this assembly, computed in ONE pass and
        ///     cached per <see cref="Compilation" />.
        ///     <para>
        ///         <see cref="HasDerivedTypesInCompilation" /> used to walk every type in the assembly on each call, and
        ///         its call site sits in per-MEMBER auto-nest resolution — before the nested-registry dedupes the pair —
        ///         so the full walk repeated for every member that reached that branch, not merely once per distinct
        ///         type pair. On a large assembly that is O(members x types) of pure re-derivation at compile time.
        ///     </para>
        ///     Keyed by a <see cref="ConditionalWeakTable{TKey,TValue}" /> so the entry (and the symbols it holds)
        ///     dies with the compilation — a plain static dictionary in a long-lived generator host would pin
        ///     symbols from every compilation it ever saw.
        /// </summary>
        private static readonly ConditionalWeakTable<Compilation, HashSet<ISymbol>> BaseTypesByCompilation = new();

        private static bool TryResolveConversion(
            Compilation compilation,
            ITypeSymbol srcType,
            ITypeSymbol tgtType,
            string? useMethod,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
            EnumPolicy enumPolicy,
            Dictionary<string, SynthesizedMethod> synthesized,
            NullStrategy nullStrategy,
            LocationInfo? location,
            string targetName,
            List<DiagnosticInfo> diagnostics,
            out string? converterMethod,
            out NullHandling nullHandling,
            out bool converterNeedsCtx,
            out string? converterParamTypeFqn,
            bool autoNest = false,
            NestedMappingRegistry? nestedRegistry = null,
            bool nullAsNull = false,
            bool isPreserve = false,
            bool allowInterfaceSrc = false,
            bool isSetNull = false,
            bool implicitConversions = true,
            IReadOnlyCollection<string>? reservedConverters = null)
        {
            converterMethod = null;
            nullHandling = NullHandling.None;
            converterNeedsCtx = false;
            // The parameter type of the DECLARED method a name was adopted from, set only where a declared name
            // becomes the converter (Use= and auto-adoption, below). An overloaded name is a different method per
            // parameter type, and the recursion-cycle phase cannot tell which one a bare name meant — see
            // MemberMap.ConverterParamTypeFqn.
            converterParamTypeFqn = null;

            // One bundle for the arms below. Five of the six read 19 or 20 of this method's parameters, so
            // passing them individually would give each arm a signature nobody could read. diagnostics and
            // synthesized stay OUT of it: they are sinks, not inputs -- see ConversionRequest.
            var req = new ConversionRequest(compilation,
                srcType,
                tgtType,
                useMethod,
                allMethods,
                autoCandidates,
                enumPolicy,
                nullStrategy,
                location,
                targetName,
                autoNest,
                nestedRegistry,
                nullAsNull,
                isPreserve,
                allowInterfaceSrc,
                isSetNull,
                implicitConversions,
                reservedConverters);

            if (useMethod is not null)
            {
                foreach (var m in allMethods)
                    if (string.Equals(m.Name, useMethod, StringComparison.Ordinal) && HasImplicitConversion(compilation, srcType, m.ParamType) && HasImplicitConversion(compilation, m.ReturnType, tgtType))
                    {
                        // B4 / DWARF032: Under Preserve mode, a Use= converter pointing to an
                        // arbitrary user function cannot participate in reference-identity tracking —
                        // the generator does not own its body and cannot thread DwarfRefContext into it.
                        // A shared/cyclic reference-type object routed through this method will be
                        // duplicated rather than de-duplicated, silently producing wrong topology.
                        // Only fire for REFERENCE-TYPE targets: scalars (int, Guid, enum, string, etc.)
                        // are never tracked by the identity map and Use= on a scalar is fine.
                        if (isPreserve && tgtType.IsReferenceType)
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                DiagnosticDescriptors.ReferenceHandlingUseConverter,
                                location,
                                targetName));
                            // Report the diagnostic AND return false so no silent wrong code is emitted.
                            return false;
                        }

                        converterMethod = m.Name;
                        converterParamTypeFqn = m.ParamType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        return true;
                    }

                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UseMethodInvalid, location, useMethod));
                return false;
            }

            if (HandleDictionaryConversion(req, diagnostics, synthesized, ref converterMethod, ref converterNeedsCtx, out var dictionaryResolved))
            {
                return dictionaryResolved;
            }

            if (HandleCollectionConversion(req, diagnostics, synthesized, ref converterMethod, ref converterNeedsCtx, out var collectionResolved))
            {
                return collectionResolved;
            }

            // ── DWARF027: target is collection/dict-shaped but not in the supported taxonomy ──
            // Check this BEFORE the implicit-conversion / object-field-mapping fallbacks so the user
            // gets a loud diagnostic instead of a wrong (or silent) mapping.
            if (IsUnsupportedCollectionTarget(tgtType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnsupportedCollectionTarget,
                    location,
                    targetName));
                return false;
            }

            if (HasImplicitConversion(compilation, srcType, tgtType))
            {
                // Cross-category numeric (integer ↔ floating/decimal, e.g. int→double, int→float) is implicit
                // in C# but crosses kinds (and int→float / long→double silently lose precision). Same-category
                // widening (int→long, float→double) is NOT flagged. DWARF038: suggestion / strict-mode error.
                if (NumericConverter.IsCrossCategoryLossy(srcType, tgtType))
                {
                    EmitImplicitConversionDiag(diagnostics,
                        location,
                        targetName,
                        srcType,
                        tgtType,
                        "cross-category numeric",
                        implicitConversions,
                        true);
                }

                return true; // direct assignment
            }

            // Nullable-value source into a target that CAN HOLD NULL: T? → U? (both Nullable<>) or T? → U?
            // (a nullable-annotated reference target) with a non-implicit inner T→U. Null-preserving
            // (null → null). Must come before the source-nullable branch so that a nullable source with a
            // synthesized inner conversion resolves to NullableProject rather than ThrowIfNull/ValueOrDefault.
            //
            // The gate used to demand BOTH sides be Nullable<T>, which made the lift depend on the DESTINATION'S
            // KIND rather than on whether it can hold the null: `S1? → D1?` lifted when D1 was a struct and threw
            // ("Source member 'X' was null") when D1 was a class or a record — an inconsistency no user can predict
            // from the types, and undocumented (the NullStrategy contract governs nullable-value source →
            // NON-nullable target). TASKS.md I7 / round 23 N2. Nullable-capable is deliberately annotation-strict
            // for references: an unannotated or oblivious reference target keeps the documented throw.
            if (HandleNullableCapableTarget(req, diagnostics, synthesized, ref converterMethod, ref nullHandling, out var nullableTargetResolved))
            {
                return nullableTargetResolved;
            }

            // Inner unresolved or has no converter (implicit, already caught above) — fall through.
            if (HandleNullableValueSource(req, diagnostics, synthesized, ref converterMethod, ref nullHandling, out var nullableSourceResolved))
            {
                return nullableSourceResolved;
            }

            // Target-nullable composition: non-Nullable<> src → T? (Nullable<> target).
            // The source is not a Nullable<T>, so resolve src→underlying and let the implicit T→T? lift do the
            // rest (valid C# assignment). A nullable-VALUE source is handled by the nullable-capable-target
            // branch above.
            //
            // The source may still be a possibly-null REFERENCE, and then the lift has to be explicit: the
            // converter is a synthesized nested mapper with a value-type return, which cannot answer null, so
            // it threw ("Cannot map a null 'S' to value-type 'D'.") on a null the Nullable<U> destination could
            // have held — I7's reverse genre, and the mirror of the kind-instead-of-capability confusion the
            // gate above had. NullableProjectRef makes the CALL SITE test for null first, which is the only
            // place that can: the helper's return type leaves it no way to express the answer.
            // TASKS.md I7 / round 23 N2.
            if (HandleTargetNullableComposition(req, diagnostics, synthesized, ref converterMethod, ref nullHandling, out var targetNullableResolved))
            {
                return targetNullableResolved;
            }

            // User-provided auto-candidate methods (no Use= annotation, auto-matched by type).
            // Checked BEFORE built-in synthesized converters (NumericConverter, ParsableConverter)
            // so that a user method can intentionally shadow the built-in behavior. The search itself is
            // FindUserDeclaredConversion — shared verbatim with the array/list blit gate, which has to ask
            // the same question one arm earlier (round 29 T0.2c); see that method's remarks.
            FindUserDeclaredConversion(req, out var found, out var foundParamType, out var ambiguous);
            if (ambiguous)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConversion,
                    location,
                    targetName));
                return false;
            }

            if (found is not null && !PrefersSynthesizedObjectMap(req, found))
            {
                converterMethod = found;
                converterParamTypeFqn = foundParamType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                nullHandling = UserConverterNullGuard(req, found);
                return true;
            }
            // A found name that PrefersSynthesizedObjectMap claims falls through to the auto-nest arm, which
            // synthesizes the private __DwarfMap_Obj_* form instead.

            // Integral↔integral narrowing / sign-change: emit CreateChecked (throws on overflow).
            // Must come after the implicit-conversion check (widening uses direct assign, not this)
            // and after user auto-candidates (user methods take precedence over built-in synthesis).
            // Enums have SpecialType.None — IsIntegral is false for them, so this never intercepts enums.
            var numericMethod = NumericConverter.TryCreate(srcType, tgtType, synthesized);
            if (numericMethod is not null)
            {
                converterMethod = numericMethod;
                // DWARF038: a non-implicit numeric conversion (narrowing / sign-change, via CreateChecked) is
                // a non-lossless basic-type conversion. Surface it as a suggestion (permissive) or a build
                // error (ImplicitConversions = false). Lossless widening uses the implicit/direct path above
                // and is never flagged.
                EmitImplicitConversionDiag(diagnostics,
                    location,
                    targetName,
                    srcType,
                    tgtType,
                    "numeric (narrowing/sign-change)",
                    implicitConversions,
                    true);
                return true;
            }

            // string↔T via IParsable<T>.Parse / IFormattable.ToString (InvariantCulture, loud on bad input).
            // Wired AFTER autoCandidates (explicit Use= and auto-conversion methods still win) and BEFORE
            // EnumConverter (enum↔string routes through EnumConverter's by-name switch; ParsableConverter
            // guards against enum operands explicitly).
            var parsableMethod = ParsableConverter.TryCreate(compilation, srcType, tgtType, synthesized);
            if (parsableMethod is not null)
            {
                converterMethod = parsableMethod;
                // DWARF038: string↔T parse/format is a non-lossless basic-type conversion → suggestion / error.
                EmitImplicitConversionDiag(diagnostics,
                    location,
                    targetName,
                    srcType,
                    tgtType,
                    "parse/format (string↔T)",
                    implicitConversions,
                    true);
                return true;
            }

            var enumMethod = EnumConverter.TryCreate(srcType,
                tgtType,
                enumPolicy,
                synthesized,
                location,
                targetName,
                diagnostics);
            if (enumMethod is not null)
            {
                converterMethod = enumMethod;
                return true;
            }

            // ── Auto-synthesized nested object mapper ─────────────────────────────
            // Placed LAST before DWARF005: only fires when nothing else resolved the pair.
            // Gate: autoNest=true AND both types are mappable named object types.
            if (HandleAutoNestedObjectMap(req, diagnostics, ref converterMethod, out var autoNestedResolved))
            {
                return autoNestedResolved;
            }

            // ── User-defined conversion operators (e.g. a strong-type's `implicit operator int`) ──────────
            // The built-in classifier above excludes user-defined conversions; honor them here, LAST, so nothing
            // else is overridden. Implicit operators convert silently (the author declared them safe); explicit
            // operators are potentially lossy → DWARF038 (or a build error under ImplicitConversions = false).
            var userConv =
                UserConversionConverter.TryCreate(compilation, srcType, tgtType, synthesized, out var userConvExplicit);
            if (userConv is not null)
            {
                converterMethod = userConv;
                if (userConvExplicit)
                {
                    EmitImplicitConversionDiag(diagnostics,
                        location,
                        targetName,
                        srcType,
                        tgtType,
                        "user-defined explicit conversion operator",
                        implicitConversions);
                }

                return true;
            }

            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoImplicitConversion, location, targetName));
            return false;
        }

        // ── What the resolver WOULD answer, asked without answering it ──────────────────────────────────
        // The three queries below are the chain's own predicates, lifted out of the arms that used to state
        // them inline. They exist because HandleCollectionConversion decides the array/list blit near the TOP
        // of this chain and has to know what the auto-candidate, auto-nest and user-operator arms — all far
        // below it — would say about the ELEMENT pair, before any of them has run and without their side
        // effects (a reserved nested pair, a synthesized helper, a diagnostic).
        // Lifted rather than restated: a second copy of "would a user method be adopted here?" is exactly the
        // drift that let the blit bypass a user converter in the first place. Round 29 T0.2c.

        /// <summary>
        ///     Searches the mapper's declared methods for the conversion the auto-candidate arm would adopt for
        ///     <c>req.SrcType → req.TgtType</c>.
        /// </summary>
        /// <param name="found">the single matching method's name, or <see langword="null" /> when none matched.</param>
        /// <param name="ambiguous">
        ///     <see langword="true" /> when more than one matched — the arm's DWARF013 refusal, reported by the
        ///     caller that owns the decision rather than by this query.
        /// </param>
        /// <remarks>
        ///     Two sources of user candidates: <c>AutoCandidates</c> (partial mapper methods, S → D object-level
        ///     mappers) and <c>AllMethods</c> (non-partial scalar converter helpers, e.g. <c>int Shrink(long v)</c>);
        ///     the second skips what the first already offered, so a partial mapper is not counted twice.
        ///     <para>
        ///         A method named by some <c>[MapProperty(Use = …)]</c> is RESERVED: the author dedicated it to
        ///         that one member, which is a statement of intent, not an offer of a general-purpose converter.
        ///         Without this guard it is also auto-adopted for every other member whose types happen to line up
        ///         — silently, with no diagnostic and a green build. Found migrating a real codebase: a
        ///         <c>string BuildDocumentId(Guid)</c> written for <c>Document.Id</c> (it prefixes a date) was also
        ///         applied to <c>Document.DispatchId</c>, a plain auto-matched Guid→string member, so every record
        ///         would have stored the decorated id in the plain field. An explicit <c>Use=</c> for THIS member
        ///         resolves at the top of the chain and is unaffected — only auto-adoption is blocked.
        ///     </para>
        /// </remarks>
        private static void FindUserDeclaredConversion(ConversionRequest req, out string? found, out ITypeSymbol? foundParamType, out bool ambiguous)
        {
            static bool IsReserved(IReadOnlyCollection<string>? reserved, string name)
            {
                return reserved is not null && reserved.Contains(name, StringComparer.Ordinal);
            }

            found = null;
            foundParamType = null;
            ambiguous = false;

            foreach (var c in req.AutoCandidates)
                if (!IsReserved(req.ReservedConverters, c.Name) && HasImplicitConversion(req.Compilation, req.SrcType, c.ParamType) && HasImplicitConversion(req.Compilation, c.ReturnType, req.TgtType))
                {
                    ambiguous |= found is not null;
                    foundParamType ??= c.ParamType;
                    found ??= c.Name;
                }

            // Also search all non-partial user methods (scalar converters not declared as partial mappers).
            foreach (var m in req.AllMethods)
            {
                if (IsReserved(req.ReservedConverters, m.Name))
                {
                    continue;
                }

                // Skip methods that are already in AutoCandidates (partial mapper methods).
                if (req.AutoCandidates.Any(ac => string.Equals(ac.Name, m.Name, StringComparison.Ordinal) && SymbolEqualityComparer.Default.Equals(ac.ParamType, m.ParamType) && SymbolEqualityComparer.Default.Equals(ac.ReturnType, m.ReturnType)))
                {
                    continue;
                }

                if (HasImplicitConversion(req.Compilation, req.SrcType, m.ParamType) && HasImplicitConversion(req.Compilation, m.ReturnType, req.TgtType))
                {
                    ambiguous |= found is not null;
                    foundParamType ??= m.ParamType;
                    found ??= m.Name;
                }
            }
        }

        /// <summary>
        ///     True when the auto-candidate arm, having found <paramref name="found" />, hands the pair on to the
        ///     auto-nest arm instead of adopting it.
        /// </summary>
        /// <remarks>
        ///     Plan 19 C2b: under Preserve OR SetNull mode, a found auto-candidate that is a PUBLIC partial mapper
        ///     method (from <c>AutoCandidates</c>) with auto-nest enabled yields to the synthesized private
        ///     <c>__DwarfMap_Obj_*</c> form. Public methods don't accept the shared <c>DwarfRefContext</c> —
        ///     calling one from a collection/dict helper would create a fresh context, losing identity/depth/
        ///     on-stack state and looping forever on cycles. User-provided converter HELPERS (<c>AllMethods</c>,
        ///     not <c>AutoCandidates</c>) are always respected.
        ///     <para>
        ///         The blit gate reads this too, and must: when the answer is yes the resolver's verdict is the
        ///         synthesized object map, which for a layout-identical pair is what the blit copies — so a pair
        ///         in this state is still blittable, and treating the found name as a user converter would have
        ///         cost every Preserve-mode collection of a declared struct pair its fast path for nothing.
        ///     </para>
        /// </remarks>
        private static bool PrefersSynthesizedObjectMap(ConversionRequest req, string found)
        {
            return (req.IsPreserve || req.IsSetNull) &&
                   req.AutoNest &&
                   req.NestedRegistry is not null &&
                   req.AutoCandidates.Any(ac => string.Equals(ac.Name, found, StringComparison.Ordinal)) &&
                   req.TgtType is INamedTypeSymbol namedTgt &&
                   IsMappableObjectPair(req.Compilation, req.SrcType, namedTgt);
        }

        /// <summary>
        ///     True when <see cref="HandleAutoNestedObjectMap" /> would CLAIM this pair — the condition that arm
        ///     opens with, shared so the blit gate can ask it without running it (and so the two cannot drift).
        /// </summary>
        private static bool AutoNestWouldClaim(ConversionRequest req)
        {
            return req.AutoNest &&
                   req.NestedRegistry is not null &&
                   req.TgtType is INamedTypeSymbol namedTgt &&
                   IsMappableObjectPair(req.Compilation, req.SrcType, namedTgt, req.AllowInterfaceSrc);
        }

        /// <summary>
        ///     Returns true when both <paramref name="src" /> and <paramref name="tgt" /> are named types
        ///     suitable for auto-nested-mapper synthesis. Excludes: scalars, enums, string, collection/
        ///     IEnumerable types, Nullable&lt;T&gt;, interfaces, abstract target types, and
        ///     abstract/interface source types (C2: those would silently drop derived-only members).
        ///     When <paramref name="allowInterfaceSrc" /> is <see langword="true" />, interface source types
        ///     are accepted (used by [MapDerivedType] arm resolution where the caller explicitly opts in).
        /// </summary>
        internal static bool IsMappableObjectPair(
            Compilation compilation,
            ITypeSymbol src,
            INamedTypeSymbol tgt,
            bool allowInterfaceSrc = false)
        {
            // Source must be a named type (class or struct/record, not array/pointer/etc.)
            if (src is not INamedTypeSymbol namedSrc)
            {
                return false;
            }

            // Source must be Class, Struct, or (when allowed) Interface.
            // Enums (TypeKind.Enum), delegates, arrays, etc. are always excluded.
            if (allowInterfaceSrc)
            {
                if (namedSrc.TypeKind != TypeKind.Class && namedSrc.TypeKind != TypeKind.Struct && namedSrc.TypeKind != TypeKind.Interface)
                {
                    return false;
                }
            }
            else
            {
                // Default: both must be Class or Struct (records are Class or Struct).
                // This also implicitly excludes enums (TypeKind.Enum), interfaces (TypeKind.Interface),
                // delegates, arrays, etc. — no separate enum guard needed.
                if (namedSrc.TypeKind != TypeKind.Class && namedSrc.TypeKind != TypeKind.Struct)
                {
                    return false;
                }
            }

            if (tgt.TypeKind != TypeKind.Class && tgt.TypeKind != TypeKind.Struct)
            {
                return false;
            }

            // Not scalar / special types (string, int, Guid, etc.)
            if (namedSrc.SpecialType != SpecialType.None || tgt.SpecialType != SpecialType.None)
            {
                return false;
            }

            // Not Nullable<T>
            if (namedSrc.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return false;
            }

            if (tgt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return false;
            }

            // C2: abstract SOURCE type — auto-nest would silently drop derived members.
            // We return false here so the caller can emit DWARF033 when appropriate.
            // Exception: interface sources are allowed when allowInterfaceSrc=true (caller explicitly
            // opted in via [MapDerivedType] dispatch, which is safe because the user controls the arms).
            if (!allowInterfaceSrc && namedSrc.IsAbstract)
            {
                return false;
            }

            // Not an interface target (can't construct an interface)
            if (tgt.IsAbstract)
            {
                return false;
            }

            // Not an IEnumerable / collection / dictionary — REGARDLESS of class vs struct.
            // (Struct collections like ImmutableArray<T> must NOT be object-field-mapped; they belong to
            //  CollectionConverter/DictionaryConverter, or fall to DWARF005 until supported. string is
            //  already excluded above by the SpecialType.None check.)
            if (ImplementsIEnumerable(namedSrc))
            {
                return false;
            }

            if (ImplementsIEnumerable(tgt))
            {
                return false;
            }

            // Target must have a constructible constructor (ConstructorSelector-compatible).
            // We do a lightweight check: at least one accessible non-static constructor.
            if (!tgt.InstanceConstructors.Any(c =>
                    c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic))
            {
                return false;
            }

            return true;
        }

        private static HashSet<ISymbol> BaseTypesInAssembly(Compilation compilation)
        {
            return BaseTypesByCompilation.GetValue(compilation,
                static c =>
                {
                    var set = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                    foreach (var type in AllTypesIn(c.Assembly.GlobalNamespace))
                        for (var b = type.BaseType; b is not null; b = b.BaseType)
                            set.Add(b.OriginalDefinition);
                    return set;
                });
        }

        private static bool HasDerivedTypesInCompilation(Compilation compilation, ITypeSymbol src)
        {
            if (src is not INamedTypeSymbol { TypeKind: TypeKind.Class } namedSrc)
            {
                return false;
            }

            if (namedSrc.IsSealed || namedSrc.IsAbstract)
            {
                return false;
            }

            if (namedSrc.SpecialType != SpecialType.None)
            {
                return false;
            }

            // "Something derives from namedSrc" — a type is never its own BaseType, so the self-comparison the old
            // loop had to make is structurally impossible here.
            return BaseTypesInAssembly(compilation).Contains(namedSrc.OriginalDefinition);
        }

        /// <summary>Every named type declared in this assembly, walking nested types too.</summary>
        private static IEnumerable<INamedTypeSymbol> AllTypesIn(INamespaceOrTypeSymbol root)
        {
            foreach (var member in root.GetMembers())
                switch (member)
                {
                    case INamespaceSymbol ns:
                        foreach (var t in AllTypesIn(ns)) yield return t;
                        break;

                    case INamedTypeSymbol type:
                        yield return type;
                        foreach (var t in AllTypesIn(type)) yield return t;
                        break;
                }
        }

        internal static bool IsAbstractOrInterfaceAutoNestSource(
            Compilation compilation,
            ITypeSymbol src,
            INamedTypeSymbol tgt)
        {
            if (src is not INamedTypeSymbol namedSrc)
            {
                return false;
            }

            // Must pass all other IsMappableObjectPair gates except the IsAbstract-source check
            if (namedSrc.TypeKind != TypeKind.Class && namedSrc.TypeKind != TypeKind.Struct)
            {
                return false;
            }

            if (tgt.TypeKind != TypeKind.Class && tgt.TypeKind != TypeKind.Struct)
            {
                return false;
            }

            if (namedSrc.SpecialType != SpecialType.None || tgt.SpecialType != SpecialType.None)
            {
                return false;
            }

            if (namedSrc.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return false;
            }

            if (tgt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return false;
            }

            if (tgt.IsAbstract)
            {
                return false;
            }

            if (ImplementsIEnumerable(namedSrc))
            {
                return false;
            }

            if (ImplementsIEnumerable(tgt))
            {
                return false;
            }

            if (!tgt.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic))
            {
                return false;
            }

            // The source is abstract (class abstract) or interface — this is the C2 trigger
            return namedSrc.IsAbstract;
        }

        /// <summary>
        ///     Returns true when <paramref name="type" /> implements <c>IEnumerable</c> (generic or non-generic),
        ///     which means it is a collection/sequence type that belongs to CollectionConverter/DictionaryConverter.
        /// </summary>
        internal static bool ImplementsIEnumerable(INamedTypeSymbol type)
        {
            // Fast checks: well-known collection / dict names (all supported + well-known unsupported)
            if (type.Name is "List" or "Array" or "HashSet" or "Dictionary"
                or "IEnumerable" or "ICollection" or "IList"
                or "IReadOnlyList" or "IReadOnlyCollection"
                or "ISet" or "IReadOnlySet"
                or "ImmutableArray" or "ImmutableList" or "IImmutableList"
                or "ImmutableHashSet" or "IImmutableSet"
                or "ImmutableDictionary" or "IImmutableDictionary"
                or "IDictionary" or "IReadOnlyDictionary"
                // well-known unsupported (DWARF027)
                or "SortedSet" or "SortedDictionary" or "SortedList"
                or "Queue" or "Stack" or "LinkedList"
                or "ConcurrentDictionary" or "ConcurrentQueue" or "ConcurrentStack" or "ConcurrentBag")
            {
                return true;
            }

            // Check whether the type or any of its interfaces is IEnumerable<T> or IEnumerable
            foreach (var iface in type.AllInterfaces)
            {
                if (iface.SpecialType == SpecialType.System_Collections_IEnumerable)
                {
                    return true;
                }

                if (iface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Returns true when <paramref name="type" /> is collection-shaped (implements IEnumerable,
        ///     is not string, is not already handled by CollectionConverter or DictionaryConverter)
        ///     → should emit DWARF027 rather than DWARF005.
        /// </summary>
        internal static bool IsUnsupportedCollectionTarget(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_String)
            {
                return false;
            }

            // Multi-dimensional array (not IArrayTypeSymbol with Rank > 1 check needed here)
            if (type is IArrayTypeSymbol arr && arr.Rank > 1)
            {
                return true;
            }

            if (type is not INamedTypeSymbol named)
            {
                return false;
            }

            // Check if it implements IEnumerable (non-string collection-shaped)
            foreach (var iface in named.AllInterfaces)
            {
                if (iface.SpecialType == SpecialType.System_Collections_IEnumerable)
                {
                    return true;
                }

                if (iface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                {
                    return true;
                }
            }

            // Also check the type itself (IEnumerable<T> as named type)
            if (named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Every method name dedicated to a member by a <c>Use=</c> anywhere on this mapper — class-level
        ///     pair-scoped attributes and per-method ones alike.
        /// </summary>
        /// <remarks>
        ///     These are withheld from signature-based auto-adoption. Naming a converter for a member states
        ///     that it belongs to that member; it is not an offer to convert every pair of those types. The
        ///     scan is deliberately generic over the named argument rather than per-attribute-type, so it
        ///     covers <c>[MapProperty]</c>, <c>[MapValue]</c> and their generic pair-scoped forms uniformly,
        ///     and keeps working if another attribute gains a <c>Use=</c>.
        /// </remarks>
        /// <summary>
        ///     True when the symbol carries <c>[SuppressMessage(…, "DWARFxxx…")]</c> for the given id.
        /// </summary>
        /// <remarks>
        ///     Generator-reported diagnostics do not pass through Roslyn's pragma filtering, so
        ///     <c>#pragma warning disable</c> cannot reach them — that is a platform limitation, not something
        ///     this generator can fix. <c>[SuppressMessage]</c> however is an ordinary attribute the generator
        ///     can read, so honouring it restores an in-FILE escape hatch next to the deliberate code, instead
        ///     of forcing an <c>.editorconfig</c> entry that disables the rule for the whole project.
        ///     <para>
        ///         The checkId is matched by prefix because the conventional spelling carries a description
        ///         after a colon: <c>"DWARF076:Source and target are the same type"</c>.
        ///     </para>
        /// </remarks>
        private static bool HasSuppressMessage(ISymbol symbol, string diagnosticId)
        {
            foreach (var a in symbol.GetAttributes())
            {
                if (!string.Equals(KnownNames.AttributeSimpleName(a.AttributeClass), "SuppressMessageAttribute", StringComparison.Ordinal))
                {
                    continue;
                }

                if (a.ConstructorArguments.Length < 2)
                {
                    continue;
                }

                if (a.ConstructorArguments[1].Value is string checkId && (string.Equals(checkId, diagnosticId, StringComparison.Ordinal) || checkId.StartsWith(diagnosticId + ":", StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<string> CollectReservedConverterNames(INamedTypeSymbol classSymbol)
        {
            var reserved = new HashSet<string>(StringComparer.Ordinal);

            static void Scan(HashSet<string> into, ImmutableArray<AttributeData> attrs)
            {
                foreach (var a in attrs)
                foreach (var na in a.NamedArguments)
                    if (string.Equals(na.Key, "Use", StringComparison.Ordinal) && na.Value.Value is string s && !string.IsNullOrEmpty(s))
                    {
                        into.Add(s);
                    }
            }

            // [MapConstructor<S,T>("Factory")] names the factory as a CONSTRUCTOR argument, not a named one,
            // so the Use= scan above cannot see it. It must be reserved for the same reason and with more
            // force: a factory only CONSTRUCTS its pair's target, after which the pair assigns members.
            // Adopted as a general element converter it produces an object with nothing filled in — the
            // collection case that surfaced this returned an Empty singleton, so a whole collection mapped
            // to blanks, silently and with a green build.
            static void ScanCtorFactories(HashSet<string> into, ImmutableArray<AttributeData> attrs)
            {
                foreach (var a in attrs)
                {
                    if (!string.Equals(KnownNames.AttributeSimpleName(a.AttributeClass), KnownNames.MapConstructor, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var ca in a.ConstructorArguments)
                        if (ca.Value is string s && !string.IsNullOrEmpty(s))
                        {
                            into.Add(s);
                        }
                }
            }

            Scan(reserved, classSymbol.GetAttributes());
            ScanCtorFactories(reserved, classSymbol.GetAttributes());
            foreach (var member in classSymbol.GetMembers())
            {
                Scan(reserved, member.GetAttributes());
                ScanCtorFactories(reserved, member.GetAttributes());
            }

            return reserved;
        }

        private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> CollectMethods(
            INamedTypeSymbol classSymbol)
        {
            var methods = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>();
            foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
                if (m.MethodKind == MethodKind.Ordinary && !m.ReturnsVoid && m.Parameters.Length == 1)
                {
                    methods.Add((m.Name, m.Parameters[0].Type, m.ReturnType));
                }

            return methods;
        }

        /// <summary>
        ///     Collects parameterless, non-void methods on the mapper — the candidate value providers for
        ///     <c>[MapValue(Use = nameof(...))]</c>. Returns <c>(Name, ReturnType)</c> pairs.
        /// </summary>
        private static List<(string Name, ITypeSymbol ReturnType)> CollectValueProviders(INamedTypeSymbol classSymbol)
        {
            var providers = new List<(string Name, ITypeSymbol ReturnType)>();
            foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
                if (m.MethodKind == MethodKind.Ordinary && !m.ReturnsVoid && m.Parameters.Length == 0)
                {
                    providers.Add((m.Name, m.ReturnType));
                }

            return providers;
        }

        private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> CollectMapperMethods(
            INamedTypeSymbol classSymbol)
        {
            var methods = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>();
            foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
                if (m.MethodKind == MethodKind.Ordinary &&
                    m.IsPartialDefinition &&
                    !m.ReturnsVoid &&
                    m.Parameters.Length == 1 &&
                    m.ReturnType is INamedTypeSymbol)
                {
                    methods.Add((m.Name, m.Parameters[0].Type, m.ReturnType));
                }

            return methods;
        }

        /// <summary>
        ///     Turns the class's <c>[GenerateMap&lt;S,T&gt;]</c> pairs into resolution candidates, so an element or
        ///     member of those types routes through the declared pair instead of a freshly synthesized mapper that
        ///     cannot see its configuration.
        /// </summary>
        /// <remarks>
        ///     A pair that a declared partial method already covers is skipped rather than added alongside it.
        ///     Seeding both would put two matching candidates in front of the ambiguity check and turn a shape
        ///     that is legal today — declare the pair, then declare a partial method for it — into a
        ///     <c>DWARF013</c>. The declared method wins, which is the same precedence the rest of resolution uses.
        /// </remarks>
        private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> GeneratedPairCandidates(
            IReadOnlyList<(ITypeSymbol Src, ITypeSymbol Tgt)> genPairs,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> declared)
        {
            var candidates = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>();
            foreach (var (src, tgt) in genPairs)
            {
                var alreadyDeclared = false;
                foreach (var d in declared)
                    if (SymbolEqualityComparer.Default.Equals(d.ParamType, src) && SymbolEqualityComparer.Default.Equals(d.ReturnType, tgt))
                    {
                        alreadyDeclared = true;
                        break;
                    }

                // Also skip a pair we have already added: [GenerateMap] twice over the same types is a
                // duplicate, not an ambiguity, and it must not become one here.
                if (!alreadyDeclared)
                {
                    foreach (var c in candidates)
                        if (SymbolEqualityComparer.Default.Equals(c.ParamType, src) && SymbolEqualityComparer.Default.Equals(c.ReturnType, tgt))
                        {
                            alreadyDeclared = true;
                            break;
                        }
                }

                // "Map" is the name the [GenerateMap] emission loop gives every pair it produces.
                if (!alreadyDeclared)
                {
                    candidates.Add(("Map", src, tgt));
                }
            }

            return candidates;
        }

        /// <summary>
        ///     The candidate list minus the pair being resolved.
        /// </summary>
        /// <remarks>
        ///     Only matters for a pair resolved as a whole rather than member by member — a top-level collection,
        ///     dictionary or value-like <c>[GenerateMap]</c>. Left in, the pair finds ITSELF:
        ///     <c>
        ///         [GenerateMap&lt;int,
        ///         long&gt;]
        ///     </c>
        ///     would resolve its own <c>Map</c> as its converter and emit <c>return Map(src);</c>,
        ///     which compiles, reports nothing, and recurses until the stack ends.
        /// </remarks>
        private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> ExcludingPair(
            List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> candidates,
            ITypeSymbol src,
            ITypeSymbol tgt)
        {
            var result = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>(candidates.Count);
            foreach (var c in candidates)
                if (!SymbolEqualityComparer.Default.Equals(c.ParamType, src) || !SymbolEqualityComparer.Default.Equals(c.ReturnType, tgt))
                {
                    result.Add(c);
                }

            return result;
        }

        /// <summary>
        ///     The candidate list minus ONE named method — the one currently being resolved.
        /// </summary>
        /// <remarks>
        ///     Narrower than <see cref="ExcludingPair" /> and necessary where a SIBLING may legitimately share the
        ///     signature. A <c>[MapDerivedType]</c> dispatcher and its base arm are exactly that: two methods with
        ///     the same parameter and return types and different names, which C# allows and <c>DWARF060</c> does
        ///     not object to. Excluding by signature removed the arm along with the dispatcher, so the arm had
        ///     nowhere to resolve and the pair failed its completeness gate on members the sibling configures.
        ///     Found by converting a CLEAN-architecture corpus off AutoMapper, where a base and a derived source
        ///     mapping to one DTO is the ordinary shape rather than an edge case.
        /// </remarks>
        private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> ExcludingMethod(
            List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> candidates,
            string name,
            ITypeSymbol src,
            ITypeSymbol tgt)
        {
            var result = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>(candidates.Count);
            foreach (var c in candidates)
                if (!string.Equals(c.Name, name, StringComparison.Ordinal) || !SymbolEqualityComparer.Default.Equals(c.ParamType, src) || !SymbolEqualityComparer.Default.Equals(c.ReturnType, tgt))
                {
                    result.Add(c);
                }

            return result;
        }

        private static (List<(string Name, ITypeSymbol ParamType)> Before,
            List<(string Name, ITypeSymbol P0, ITypeSymbol? P1, RefKind TargetRefKind)> After)
            CollectHooks(INamedTypeSymbol classSymbol, List<DiagnosticInfo> diagnostics)
        {
            var before = new List<(string Name, ITypeSymbol ParamType)>();
            var after = new List<(string Name, ITypeSymbol P0, ITypeSymbol? P1, RefKind TargetRefKind)>();
            foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                var isBefore = m.GetAttributes()
                    .Any(a => KnownNames.IsAttributeClass(a.AttributeClass, KnownNames.BeforeMapFqn));
                var isAfter = m.GetAttributes()
                    .Any(a => KnownNames.IsAttributeClass(a.AttributeClass, KnownNames.AfterMapFqn));
                if (!isBefore && !isAfter)
                {
                    continue;
                }

                var loc = LocationInfo.FromFirst(m.Locations);

                // A partial declaration whose implementing part is absent has no body: C# erases it and every call
                // to it, and where THIS generator supplies the missing part the call is into the method being
                // generated. Either way there is nothing for a hook to run, so it is refused (DWARF091) before the
                // signature is looked at — the shape is wrong regardless of what the signature happens to be, and
                // checking void-ness first produced three different wrong answers for one mistake: DWARF018 on the
                // create map, silence on the span map, and `Update(s, d);` emitted inside `Update` on the update
                // map. See the descriptor's remarks, and surface-matrix finding D16.
                if (m.IsPartialDefinition && m.PartialImplementationPart is null)
                {
                    // Once per attribute written, not once per method: the two are separate mistakes with
                    // separate remedies, and a method can carry both.
                    if (isBefore)
                    {
                        diagnostics.Add(NoBodyHook("BeforeMap", "void Hook(TSource)"));
                    }

                    if (isAfter)
                    {
                        diagnostics.Add(NoBodyHook("AfterMap", "void Hook(TTarget) or void Hook(TSource, TTarget)"));
                    }

                    continue;

                    DiagnosticInfo NoBodyHook(string attribute, string shape)
                    {
                        return new DiagnosticInfo(
                            DiagnosticDescriptors.HookOnMethodWithNoBody,
                            loc,
                            $"[{attribute}] is written on '{m.Name}', a partial method with no implementing part, so " +
                            "it has no body to run — C# erases such a method and every call to it, and where " +
                            "DwarfMapper supplies the missing part the call would re-enter the method being " +
                            $"generated. The hook is not registered. Move [{attribute}] to an ordinary method on the " +
                            $"mapper with the hook shape ({shape}), or remove it.");
                    }
                }

                if (!m.ReturnsVoid)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidHook, loc, m.Name));
                    continue;
                }

                if (isBefore)
                {
                    if (m.Parameters.Length != 1)
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidHook, loc, m.Name));
                    }
                    else
                    {
                        before.Add((m.Name, m.Parameters[0].Type));
                    }
                }

                if (isAfter)
                {
                    if (m.Parameters.Length == 1)
                        // 1-param: the sole parameter is the target; capture its RefKind
                    {
                        after.Add((m.Name, m.Parameters[0].Type, null, m.Parameters[0].RefKind));
                    }
                    else if (m.Parameters.Length == 2)
                        // 2-param: P0=source, P1=target; capture P1's RefKind
                    {
                        after.Add((m.Name, m.Parameters[0].Type, m.Parameters[1].Type, m.Parameters[1].RefKind));
                    }
                    else
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidHook, loc, m.Name));
                    }
                }
            }

            return (before, after);
        }

        private static bool HasImplicitConversion(Compilation compilation, ITypeSymbol source, ITypeSymbol target)
        {
            var conversion = ((CSharpCompilation)compilation).ClassifyConversion(source, target);
            return conversion.IsImplicit && !conversion.IsUserDefined;
        }

        private static bool IsNullableValue(ITypeSymbol type, out ITypeSymbol underlying)
        {
            if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                underlying = named.TypeArguments[0];
                return true;
            }

            underlying = type;
            return false;
        }

        /// <summary>
        ///     Returns true when <paramref name="type" /> is a destination that CAN HOLD NULL — either a
        ///     <c>Nullable&lt;U&gt;</c> or a nullable-annotated reference — and yields the type a converter must
        ///     produce (<c>U</c> for <c>Nullable&lt;U&gt;</c>; the un-annotated reference type otherwise).
        ///     <para>
        ///         This is the predicate that decides whether a nullable source may be LIFTED (null → null) rather
        ///         than unwrapped with <c>?? throw</c>. It asks the only question that matters — can the destination
        ///         store the null? — instead of the destination's KIND, which is what made the same member throw or
        ///         lift depending on whether the mirrored node happened to be a struct or a class (TASKS.md I7).
        ///     </para>
        ///     <para>
        ///         Annotation-strict on the reference side on purpose: an un-annotated (or oblivious) reference target
        ///         is a promise that it holds no null, and the documented <c>NullStrategy</c> contract — nullable
        ///         source into a non-nullable target throws, or takes the default — continues to govern it.
        ///     </para>
        /// </summary>
        private static bool TryGetNullableCapableTarget(ITypeSymbol type, out ITypeSymbol underlying)
        {
            if (IsNullableValue(type, out underlying))
            {
                return true;
            }

            if (IsNullableReferenceType(type))
            {
                // Hand the converter resolver the un-annotated form: the conversion S → D is the same question
                // whether or not the destination slot is annotated, and stripping it keeps converter matching
                // (which compares symbols) from depending on an annotation the resolver does not model.
                underlying = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                return true;
            }

            underlying = type;
            return false;
        }

        /// <summary>
        ///     Returns true when <paramref name="type" /> is a nullable reference type
        ///     (i.e. a reference type with <c>NullableAnnotation.Annotated</c>), e.g. <c>List&lt;int&gt;?</c>.
        ///     Used to decide whether AsNull semantics are safe for a given target field (A3).
        /// </summary>
        private static bool IsNullableReferenceType(ITypeSymbol type)
        {
            return type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated;
        }


        /// <summary>
        ///     True when a source member of this type may be null from the compiler's point of view — a
        ///     reference type that is nullable-annotated OR oblivious (<c>#nullable disable</c> context).
        ///     Drives the null-forgiving <c>!</c> at synthesized-converter call sites: only such sources can
        ///     trip CS8604 when the converter's parameter is non-nullable. Value types (enums, numerics,
        ///     <c>Nullable&lt;T&gt;</c>) and non-nullable references never need it, so the emitter omits the
        ///     otherwise-spurious <c>!</c> for them. <see cref="MemberMap.SourceIsNullableRef" />.
        /// </summary>
        private static bool SourceMayBeNullRef(ITypeSymbol type)
        {
            return type.IsReferenceType && type.NullableAnnotation != NullableAnnotation.NotAnnotated;
        }

        /// <summary>
        ///     True when a nullable-annotated REFERENCE source is being assigned to a NON-nullable reference
        ///     target — i.e. exactly the case in which the C# compiler raises CS8601 on the generated assignment.
        ///     Drives DWARF070 and the null-forgiving <c>!</c> that keeps CS8601 out of the generated file.
        ///     <para>
        ///         Deliberately strict on BOTH sides (<c>== Annotated</c> / <c>== NotAnnotated</c>) rather than the
        ///         looser <c>!= NotAnnotated</c> used by <see cref="SourceMayBeNullRef" />. NullableAnnotation has
        ///         three states, and the third — <c>None</c>, "oblivious", a type written in a <c>#nullable disable</c>
        ///         context — means the user opted out of nullable analysis. The compiler emits no CS8601 there, so
        ///         neither do we: an oblivious codebase would otherwise be flooded with warnings about a contract it
        ///         never opted into. This predicate tracks the compiler exactly.
        ///     </para>
        /// </summary>
        private static bool NullRefIntoNonNullableRef(ITypeSymbol srcType, ITypeSymbol tgtType)
        {
            return srcType.IsReferenceType && srcType.NullableAnnotation == NullableAnnotation.Annotated && tgtType.IsReferenceType && tgtType.NullableAnnotation == NullableAnnotation.NotAnnotated;
        }

        /// <summary>
        ///     True when this member resolves to a <b>raw</b> assignment of a nullable reference into a
        ///     non-nullable one — the only shape that actually emits CS8601. A converter or a NullHandling
        ///     strategy means the emitter routes the value through something that deals with the null
        ///     (<c>Conv(src.X!)</c>, <c>src.X ?? throw</c>, …), so neither the <c>!</c> nor DWARF070 applies there.
        /// </summary>
        private static bool IsDirectNullRefAssign(
            string? converterMethod,
            NullHandling nullHandling,
            ITypeSymbol srcType,
            ITypeSymbol tgtType)
        {
            return converterMethod is null && nullHandling == NullHandling.None && NullRefIntoNonNullableRef(srcType, tgtType);
        }

        /// <summary>
        ///     True when <paramref name="converterMethod" /> resolves to a USER-declared map/converter method whose
        ///     parameter is a non-nullable reference type. This is the fact the emitter's <c>IsSynthesized</c> proxy
        ///     stood in for: a synthesized nested mapper (<c>__DwarfMap_Obj_</c>) has a non-nullable parameter and
        ///     null-guards internally, so a nullable source argument is null-forgiven; a user-declared nested map
        ///     (<c>MapFlat(FlatSrc s)</c>) has the same non-nullable parameter but is invisible to
        ///     <c>IsSynthesized</c>, so its call was left as <c>MapFlat(s.Inner)</c> — CS8604. Looking the converter
        ///     up in the user's own method lists recovers the parameter nullability the proxy discarded.
        ///     <para>
        ///         A null-TOLERANT user converter (declared with a nullable parameter) returns false here, so it is never
        ///         forgiven and keeps receiving the null it was written to accept.
        ///     </para>
        /// </summary>
        private static bool ConverterParamIsNonNullableRef(
            string? converterMethod,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods)
        {
            if (converterMethod is null)
            {
                return false;
            }

            static bool IsNonNullableRefParam(ITypeSymbol p)
            {
                return p.IsReferenceType && p.NullableAnnotation == NullableAnnotation.NotAnnotated;
            }

            foreach (var m in autoCandidates)
                if (string.Equals(m.Name, converterMethod, StringComparison.Ordinal) && IsNonNullableRefParam(m.ParamType))
                {
                    return true;
                }

            foreach (var m in allMethods)
                if (string.Equals(m.Name, converterMethod, StringComparison.Ordinal) && IsNonNullableRefParam(m.ParamType))
                {
                    return true;
                }

            return false;
        }

        /// <summary>
        ///     The null handling a <b>user-declared</b> converter's call site needs, asked once where that converter
        ///     is chosen so that every edge which routes through <c>TryResolveConversion</c> — a member, a collection
        ///     element, a dictionary value, a flatten leaf — gets the same answer.
        ///     <para>
        ///         WHY THIS EXISTS. A synthesized nested mapper opens with <c>if (s is null) return null!;</c>: null in,
        ///         null out. A USER-declared map method that the auto-candidate arm adopts for the same edge opens with
        ///         <c>ArgumentNullException.ThrowIfNull(source)</c> — it is also a public entry point, and that guard is
        ///         its own contract. Whether a nested edge preserved a null or threw therefore depended on which of the
        ///         two the resolver happened to pick, which is not a fact any user can predict from the types. Worse,
        ///         the call site got the null-forgiving <c>!</c> only through <see cref="ForgiveNestedNullableArg" />,
        ///         which requires <see cref="NullRefIntoNonNullableRef" /> — a NON-nullable destination — while the
        ///         null-preserving lift (<see cref="NullHandling.NullableProjectRef" />) was only ever reached for a
        ///         <c>Nullable&lt;U&gt;</c> VALUE destination. A nullable reference into a nullable reference fell
        ///         between the two and was emitted bare: <c>CS8604</c> inside the consumer's <c>.g.cs</c>, where no
        ///         <c>#pragma</c>, <c>NoWarn</c> or <c>.editorconfig</c> reaches it.
        ///     </para>
        ///     <para>
        ///         Three arms, and the third is deliberately NOT one:
        ///         <list type="bullet">
        ///             <item>
        ///                 <description>
        ///                     Destination can hold null (nullable-annotated reference) →
        ///                     <see cref="NullHandling.NullableProjectRef" />: <c>x is null ? null : Conv(x)</c>.
        ///                 </description>
        ///             </item>
        ///             <item>
        ///                 <description>
        ///                     Destination cannot, and the SOURCE is not nullable-annotated either →
        ///                     <see cref="NullHandling.NullableProjectRefForgiving" />. Both annotations claim the value
        ///                     cannot be null; a <c>Result&lt;T&gt;</c> whose <c>Fail</c> stores <c>default!</c> makes one
        ///                     of them false at run time, and mapping a failed result must not throw. Same lift, with the
        ///                     <c>!</c> that keeps CS8601 out of the generated file.
        ///                 </description>
        ///             </item>
        ///             <item>
        ///                 <description>
        ///                     Destination cannot, and the source IS nullable-annotated → <see cref="NullHandling.None" />,
        ///                     UNCHANGED. That is the shape <c>DWARF070</c> already names against the user's own DTO, and
        ///                     <see cref="ForgiveNestedNullableArg" /> already forgives the argument. Silently lifting it
        ///                     here would swallow the one case the mapper is supposed to be loud about.
        ///                 </description>
        ///             </item>
        ///         </list>
        ///     </para>
        ///     <para>
        ///         Gated on the converter's parameter being a NON-nullable reference: a user converter declared to accept
        ///         null was written to handle it, and keeps receiving the null it asked for. Value-type sources and
        ///         destinations are excluded — a value source is never null, and a non-nullable value destination cannot
        ///         express the lifted null at all (those pairs are already answered by the <c>Nullable&lt;&gt;</c> arms
        ///         further up the chain).
        ///     </para>
        /// </summary>
        private static NullHandling UserConverterNullGuard(ConversionRequest req, string converter)
        {
            if (!req.SrcType.IsReferenceType || !req.TgtType.IsReferenceType || !ConverterParamIsNonNullableRef(converter, req.AutoCandidates, req.AllMethods))
            {
                return NullHandling.None;
            }

            if (req.TgtType.NullableAnnotation == NullableAnnotation.Annotated)
            {
                return NullHandling.NullableProjectRef;
            }

            return req.SrcType.NullableAnnotation == NullableAnnotation.Annotated
                ? NullHandling.None
                : NullHandling.NullableProjectRefForgiving;
        }

        /// <summary>
        ///     The single decision for EVERY <see cref="MemberMap" /> construction site (auto-match member, explicit
        ///     [MapProperty], flatten, and both constructor-argument paths): does a nullable-reference source flowing
        ///     into a user-declared converter with a non-nullable parameter need null-forgiving, AND — coupled here so
        ///     no site can forgive without also signalling — does it warrant DWARF070? Gated on the DESTINATION being
        ///     non-nullable (<see cref="NullRefIntoNonNullableRef" />): a nullable destination legitimately propagates
        ///     the null. Emits DWARF070 exactly when it returns true, so the emitter's <c>!</c> and the diagnostic can
        ///     never diverge across sites. Returning the flag is what the emitter reads via
        ///     <c>MemberMap.ConverterParamIsNonNullableRef</c>.
        ///     See docs/superpowers/specs/2026-07-25-nested-nullable-parameter.md.
        /// </summary>
        /// <summary>
        ///     What kind of thing carries the null that <c>DWARF070</c> is about. The diagnostic's remedies are
        ///     not the same for all of them, which is the whole reason this is an enum rather than a name:
        ///     <c>[MapProperty(NullSubstitute = …)]</c> and <c>SkipNullSourceMembers</c> reach a source MEMBER
        ///     and nothing else, so naming a mapping parameter or a collection element a "member" points the
        ///     reader at two levers that cannot touch their shape.
        /// </summary>
        private enum NullSourceKind
        {
            /// <summary>A member of the source type — the original and still the common case.</summary>
            SourceMember = 0,

            /// <summary>A Phase 5 mapping PARAMETER (round 29 task 2.7).</summary>
            MappingParameter = 1,

            /// <summary>
            ///     One ELEMENT of a mapped collection, span or async stream (round 29 task 2.9). The null is in
            ///     the source element type (<c>List&lt;Child?&gt;</c>); the destination that cannot hold it is the
            ///     destination ELEMENT type, not the member.
            /// </summary>
            CollectionElement = 2,

            /// <summary>
            ///     One VALUE of a mapped dictionary (round 29 task 2.9). Same shape as
            ///     <see cref="CollectionElement" />, named separately because a reader hunting
            ///     <c>Dictionary&lt;string, Child?&gt;</c> is not looking for the word "element".
            /// </summary>
            DictionaryValue = 3
        }

        /// <summary>
        ///     How <c>DWARF070</c> names the thing that carries the null. The diagnostic's <c>{0}</c> is this
        ///     whole phrase rather than a bare name, because the same descriptor now covers four kinds of thing:
        ///     a member of the source type, a Phase 5 mapping PARAMETER, a collection/span/stream ELEMENT and a
        ///     dictionary VALUE. Calling any of the last three a "member" is not a wording nit — it points the
        ///     reader at <c>[MapProperty(NullSubstitute = …)]</c> and <c>SkipNullSourceMembers</c>, none of which
        ///     can reach them.
        ///     <para>
        ///         The element and value spellings name the DESTINATION member the edge feeds rather than a source
        ///         member: at a collection or dictionary arm the only name in scope is
        ///         <c>ConversionRequest.TargetName</c>, and for a span/async-stream endpoint the destination is a
        ///         parameter or the return type, not a member at all — so the method's name is what locates it.
        ///     </para>
        /// </summary>
        private static string NullSourceLabel(string name, NullSourceKind kind = NullSourceKind.SourceMember)
        {
            return kind switch
            {
                NullSourceKind.MappingParameter => "Mapping parameter '" + name + "'",
                NullSourceKind.CollectionElement => "The source element mapped into '" + name + "'",
                NullSourceKind.DictionaryValue => "The source value mapped into '" + name + "'",
                _ => "Source member '" + name + "'"
            };
        }

        /// <summary>
        ///     How <c>DWARF107</c> names the destination the converter's nullable return is written into — the
        ///     mirror of <see cref="NullSourceLabel" />, and here rather than at the call sites for the same
        ///     reason: the two diagnostics describe the same edges from opposite ends and must not end up naming
        ///     the same edge differently.
        /// </summary>
        private static string NullTargetLabel(string name, NullSourceKind kind = NullSourceKind.SourceMember)
        {
            return kind switch
            {
                NullSourceKind.CollectionElement => "the element type of '" + name + "'",
                NullSourceKind.DictionaryValue => "the value type of '" + name + "'",
                _ => "destination member '" + name + "'"
            };
        }

        /// <summary>
        ///     True when <paramref name="converterMethod" /> resolves to a USER-declared map/converter method whose
        ///     RETURN is a nullable-annotated reference type. The mirror of
        ///     <see cref="ConverterParamIsNonNullableRef" />, reading the third element of the same tuples, and the
        ///     fact <c>MemberMap</c> was missing entirely: it carried the argument side and nothing at all for the
        ///     result side, so <c>partial ChildDto? ToDto(Child c)</c> feeding a non-nullable <c>ChildDto Inner</c>
        ///     emitted <c>Inner = s.Inner is null ? null! : ToDto(s.Inner)</c> — the null ARM forgiven and the
        ///     CALL not, which is CS8601 in the consumer's .g.cs (round 29 task 2.8 concern 1).
        /// </summary>
        internal static bool ConverterReturnIsNullableRef(
            string? converterMethod,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods)
        {
            if (converterMethod is null)
            {
                return false;
            }

            static bool IsNullableRefReturn(ITypeSymbol r)
            {
                return r.IsReferenceType && r.NullableAnnotation == NullableAnnotation.Annotated;
            }

            foreach (var m in autoCandidates)
                if (string.Equals(m.Name, converterMethod, StringComparison.Ordinal) && IsNullableRefReturn(m.ReturnType))
                {
                    return true;
                }

            foreach (var m in allMethods)
                if (string.Equals(m.Name, converterMethod, StringComparison.Ordinal) && IsNullableRefReturn(m.ReturnType))
                {
                    return true;
                }

            return false;
        }

        /// <summary>
        ///     The RETURN-side twin of <see cref="ForgiveNestedNullableArg" />, and coupled the same way: does the
        ///     converter's result need the null-forgiving <c>!</c>, AND — decided here so no site can forgive
        ///     without signalling — does it warrant <c>DWARF107</c>?
        ///     <para>
        ///         Gated on the DESTINATION being a non-nullable-annotated reference, exactly as
        ///         <see cref="NullRefIntoNonNullableRef" /> gates its own half. A nullable destination holds the
        ///         returned null legitimately and needs neither the <c>!</c> nor the warning; a <c>!</c> where none
        ///         is owed is noise in a file the reader cannot edit. The SOURCE is deliberately not consulted —
        ///         that is the whole distinction from DWARF070, and the shape that proves it is a wholly
        ///         non-nullable pair (<c>Child Inner</c> -> <c>ChildDto Inner</c>) whose converter returns
        ///         <c>ChildDto?</c>: nothing on the source side is nullable and CS8601 is emitted anyway.
        ///     </para>
        ///     <para>
        ///         Annotation-strict on the destination for the reason recorded on
        ///         <see cref="NullRefIntoNonNullableRef" />: an oblivious destination is code that opted out of
        ///         nullable analysis, the compiler says nothing there, and neither does this.
        ///     </para>
        /// </summary>
        private static bool ForgiveConverterNullableReturn(
            string? converterMethod,
            ITypeSymbol tgtType,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
            string targetName,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            NullSourceKind kind = NullSourceKind.SourceMember)
        {
            if (converterMethod is null)
            {
                return false;
            }

            var forgive = tgtType.IsReferenceType &&
                          tgtType.NullableAnnotation == NullableAnnotation.NotAnnotated &&
                          ConverterReturnIsNullableRef(converterMethod, autoCandidates, allMethods);
            if (forgive)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.ConverterNullableReturnToNonNullableTarget,
                    location,
                    converterMethod,
                    MessageArg2: NullTargetLabel(targetName, kind)));
            }

            return forgive;
        }

        private static bool ForgiveNestedNullableArg(
            string? converterMethod,
            ITypeSymbol srcType,
            ITypeSymbol tgtType,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
            string sourceName,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            NullSourceKind kind = NullSourceKind.SourceMember)
        {
            var forgive = NullRefIntoNonNullableRef(srcType, tgtType) && ConverterParamIsNonNullableRef(converterMethod, autoCandidates, allMethods);
            if (forgive)
            {
                // kind defaults to SourceMember and the member/flatten/constructor-argument callers take that
                // default, which is CORRECT rather than convenient: every one of them resolves a member, a
                // flatten leaf or a constructor argument of the SOURCE TYPE. The extra-parameter phase and the
                // four element edges (collection, dictionary value, span, async stream) each pass their own kind
                // explicitly, because their remedy is not the source-member one.
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.NullableRefSourceToNonNullableTarget,
                    location,
                    NullSourceLabel(sourceName, kind)));
            }

            return forgive;
        }

        // Enumeration lives in Core.MemberFacts so both engines share one implementation. These wrappers keep the
        // class model's existing (Name, Type) shape so its 31 call sites are untouched by the move.
        //
        // ISSUE-044: both parameters are REQUIRED on purpose. They used to default to (null, false), and twenty
        // call sites silently took that default — so those paths answered "which members can I read?" as if the
        // mapper had never opted into AllowNonPublic, producing false DWARF043/DWARF045/DWARF001 on legal code.
        // A required parameter turns "I forgot to thread the flag" from a silent wrong answer into a compile
        // error at the call site, which is the only version of this that stays fixed.
        private static IEnumerable<(string Name, ITypeSymbol Type)> ReadableMembers(
            ITypeSymbol type,
            Compilation? compilation,
            bool allowNonPublic)
        {
            foreach (var m in MemberFacts.Readable(type, compilation, allowNonPublic))
                yield return (m.Name, m.Type);
        }

        private static IEnumerable<(string Name, ITypeSymbol Type)> WritableMembers(
            ITypeSymbol type,
            Compilation? compilation,
            bool allowNonPublic)
        {
            foreach (var m in MemberFacts.Writable(type, compilation, allowNonPublic))
                yield return (m.Name, m.Type);
        }
    }
}
