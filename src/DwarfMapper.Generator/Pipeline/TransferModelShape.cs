// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     Decides whether a class is TRANSFER-MODEL SHAPED: a type whose only job is to carry data, and which
    ///     could therefore be a <c>readonly record struct</c> instead. Reports nothing itself — <c>DWARF103</c>
    ///     is the voice, this is the predicate — and mutates nothing, so a caller may run it per mapped pair.
    ///     <para>
    ///         <b>It refuses far more than it accepts, and every refusal is the cheap side of the trade.</b>
    ///         The code fix behind this diagnostic deliberately does not touch usages: a <c>null</c> check, an
    ///         aliasing assignment and <c>list[i].X = v</c> all become compile errors on purpose, loud over
    ///         silent. That is the right bargain for a DTO and the wrong one for anything else, so a type that
    ///         is tracked by an ORM, derived from, disposed, subscribed to, or built by a constructor that
    ///         validates is refused here rather than explained later. Suggesting the rewrite wrongly costs a
    ///         consumer a broken build or, worse, a semantic change they did not read about; refusing wrongly
    ///         costs them a hint they never see.
    ///     </para>
    ///     <para>
    ///         <b>The size is of the WOULD-BE struct, not of the class object.</b> The arithmetic is
    ///         <see cref="LayoutHygiene" />'s — one implementation of the CLR's layout rules, never two — and
    ///         this file supplies only what that file cannot know: what a member is worth in a struct. A
    ///         nested transfer model is INLINE (the fix rewrites it too), an optional one is
    ///         <c>Nullable&lt;T&gt;</c> around it, and a <c>string</c>, array or <c>List&lt;T&gt;</c> stays a
    ///         reference FIELD. That last one is a policy call this file owns and flags: a reference is 8 bytes
    ///         on x64 and 4 on x86, which is precisely the platform dependence
    ///         <see cref="BlittableProof.PrimitiveSize" /> refuses to guess at for <c>IntPtr</c>. It is taken
    ///         here as an UPPER BOUND — the widest a reference is anywhere in scope — and every verdict says so
    ///         through <see cref="Verdict.SizeIsUpperBound" />. Because the bound only ever over-counts, an
    ///         <c>Eligible</c> verdict cannot become wrong on a narrower platform, only conservative; a caller
    ///         must not print such a size as exact.
    ///     </para>
    /// </summary>
    internal static class TransferModelShape
    {
        /// <summary>
        ///     A reference field's (size, alignment) on x64 — an UPPER BOUND, see the class doc. The one
        ///     number in this file that is a policy rather than a measurement, which is why it is named.
        /// </summary>
        private static readonly (int Size, int Align) ReferenceField = (8, 8);

        /// <summary>
        ///     Recursion cap over nested transfer models. A cycle is caught exactly by the path walk, so this
        ///     only bounds a graph that is deep rather than circular — the same "bounded rather than trusting
        ///     the language rule" stance <see cref="LayoutHygiene" /> takes.
        /// </summary>
        private const int MaxDepth = 16;

        /// <summary>
        ///     Bytes at or under which a struct transfer model's SIZE is not worth remarking on. Above it the
        ///     size is printed; only above <see cref="SuggestInSizeLimit" /> is <c>in</c> advised. (The
        ///     diagnostic itself fires on SHAPE, not on size, in every band.)
        ///     <para>
        ///         Assembly-visible so <c>DWARF103</c>'s message can PRINT the threshold it applied rather than
        ///         restate it as a literal. A second copy of 32 in the diagnostic would be free to drift from
        ///         the one the verdict was decided by, and the message would then name a threshold nothing
        ///         enforces (round 29, T2.2).
        ///     </para>
        /// </summary>
        internal const int SilentSizeLimit = 32;

        /// <summary>
        ///     Bytes above which the struct is reported <see cref="Outcome.TooLarge" /> to copy by value — and
        ///     the ONLY band in which <c>DWARF103</c> advises <c>in</c>. Assembly-visible for the same reason
        ///     <see cref="SilentSizeLimit" /> is: the message prints the threshold it applied rather than a
        ///     literal that could drift from it.
        /// </summary>
        internal const int SuggestInSizeLimit = 64;

        /// <summary>What <see cref="Classify(INamedTypeSymbol, Compilation)" /> concluded.</summary>
        public enum Outcome
        {
            /// <summary>Transfer-model shaped and small enough to pass by value.</summary>
            Eligible,

            /// <summary>
            ///     Transfer-model shaped, but over <see cref="SuggestInSizeLimit" /> bytes — still a struct
            ///     worth having, and THE band in which a caller is told to pass it by <c>in</c>.
            ///     <para>
            ///         Round 29 T2.2 fix round 3 corrected this sentence, which used to read "and still one a
            ///         caller should be told to pass by <c>in</c>" beside a <see cref="Verdict.SuggestIn" />
            ///         documented the same way — so both bands appeared to warrant the advice and
            ///         <c>DWARF103</c> gave it in both. The spec tiers them (≤32 B silent, 32–64 B Info, &gt;64 B
            ///         suggest <c>in</c>), the project owner ruled that tiering reasonable
            ///         (<c>Issues/round29/RESEARCH-hardware-mode.md</c> §9, ruling (b)), and the measurement
            ///         behind it is that a 64-byte struct still beat its class by value (26.4 ns vs 29.9 ns).
            ///         Advising <c>in</c> at 40 bytes would be advising an indirection the numbers do not ask
            ///         for.
            ///     </para>
            /// </summary>
            TooLarge,

            /// <summary>Not transfer-model shaped. <see cref="Verdict.Reason" /> says why, in a consumer's terms.</summary>
            NotEligible
        }

        /// <summary>
        ///     The verdict, carrying everything a diagnostic needs to word itself: the size, whether that size
        ///     is an estimate, whether to suggest <c>in</c>, and — on a refusal — a reason naming the member or
        ///     declaration that caused it.
        /// </summary>
        /// <param name="Kind">Eligible, too large to copy by value, or not shaped at all.</param>
        /// <param name="Size">
        ///     Bytes the would-be struct occupies, including trailing padding. Zero on
        ///     <see cref="Outcome.NotEligible" />, where no size was proven — never a size to print.
        /// </param>
        /// <param name="SuggestIn">
        ///     True between <see cref="SilentSizeLimit" /> and <see cref="SuggestInSizeLimit" /> bytes: the
        ///     spec's MIDDLE band, where the type is reported and its size printed but no <c>in</c> is advised.
        ///     <para>
        ///         The name is older than the rule and is now misleading, which is why this says so rather than
        ///         renaming a field T2.3 also reads: the <c>in</c> advice belongs to
        ///         <see cref="Outcome.TooLarge" /> alone (see its remarks). What this flag marks is the band
        ///         where the by-value copy has started to cost something without yet outweighing the class —
        ///         the measured crossover is around 40 bytes and a 64-byte struct still wins — so the honest
        ///         report there is the size and nothing more.
        ///     </para>
        /// </param>
        /// <param name="SizeIsUpperBound">
        ///     True when a member was counted as an 8-byte reference field. The real struct is that size on
        ///     x64 and smaller on a 32-bit runtime, so a caller must word it as a bound ("at most N bytes"),
        ///     never as a measurement.
        /// </param>
        /// <param name="DerivationCheckedWithinAssemblyOnly">
        ///     True when the "sealed, or nothing derives from it" rule was decided on evidence that does not
        ///     cover everywhere the type is visible: a <c>public</c> unsealed type can be subclassed by a
        ///     CONSUMING project, and the sweep sees this assembly only.
        ///     <para>
        ///         Restricting the rule to non-public types would end the feature — most transfer models are
        ///         public. So the check stays, and the verdict says what it actually checked instead: a caller
        ///         must not word the suggestion as though derivation had been ruled out everywhere. Telling a
        ///         consumer exactly what is wrong has a corollary, which is not implying we looked further
        ///         than we did.
        ///     </para>
        /// </param>
        /// <param name="Reason">
        ///     Why the type was refused, in terms of the consumer's own declaration — empty for the two
        ///     eligible outcomes. Always non-empty on <see cref="Outcome.NotEligible" />.
        /// </param>
        internal readonly record struct Verdict(
            Outcome Kind,
            int Size,
            bool SuggestIn,
            bool SizeIsUpperBound,
            bool DerivationCheckedWithinAssemblyOnly,
            string Reason)
        {
            /// <summary>True when the type is transfer-model shaped, whatever its size.</summary>
            public bool IsShaped => Kind != Outcome.NotEligible;

            internal static Verdict Fits(
                int size,
                bool suggestIn,
                bool sizeIsUpperBound,
                bool derivationCheckedWithinAssemblyOnly)
            {
                return new Verdict(
                    Outcome.Eligible,
                    size,
                    suggestIn,
                    sizeIsUpperBound,
                    derivationCheckedWithinAssemblyOnly,
                    string.Empty);
            }

            internal static Verdict Oversized(
                int size,
                bool sizeIsUpperBound,
                bool derivationCheckedWithinAssemblyOnly)
            {
                return new Verdict(
                    Outcome.TooLarge,
                    size,
                    false,
                    sizeIsUpperBound,
                    derivationCheckedWithinAssemblyOnly,
                    string.Empty);
            }

            internal static Verdict No(string reason)
            {
                return new Verdict(Outcome.NotEligible, 0, false, false, false, reason);
            }
        }

        /// <summary>
        ///     The two facts about a whole compilation that <see cref="Classify(INamedTypeSymbol, Compilation, CompilationFacts)" />
        ///     needs but must not go looking for: which types are derived from, and which are Entity Framework
        ///     entities by virtue of a <c>DbSet&lt;T&gt;</c> somewhere.
        ///     <para>
        ///         Both are ONE sweep of the assembly's types, and both are per-compilation rather than
        ///         per-pair. Gathering them here — once, by an explicit call the caller makes — is what keeps
        ///         <c>Classify</c> honestly pure: the alternative, a lazy cache inside the predicate, would
        ///         make its first call cost what a whole sweep costs and its behaviour depend on call order.
        ///     </para>
        ///     <para>
        ///         <b>Known limit, for the diagnostic's documentation:</b> the sweep sees this assembly only, so
        ///         a <c>DbContext</c> declared in another project does not mark its entities here. The
        ///         attribute half of the heuristic still catches those, since the attributes travel with the
        ///         entity.
        ///     </para>
        /// </summary>
        internal sealed class CompilationFacts
        {
            private readonly Dictionary<ISymbol, string> _derivedFrom;
            private readonly HashSet<ISymbol> _entities;

            private CompilationFacts(Dictionary<ISymbol, string> derivedFrom, HashSet<ISymbol> entities)
            {
                _derivedFrom = derivedFrom;
                _entities = entities;
            }

            /// <summary>How many types this compilation exposes as a <c>DbSet&lt;T&gt;</c>.</summary>
            public int EntityCount => _entities.Count;

            /// <summary>
            ///     Walks the assembly once, collecting both facts.
            ///     <para>
            ///         The <c>DbSet&lt;T&gt;</c> half short-circuits entirely when the compilation has no such
            ///         type — a compilation that does not reference EF Core cannot contain one, which is most
            ///         of them, and that check is a single metadata lookup against a whole member walk.
            ///     </para>
            /// </summary>
            public static CompilationFacts Gather(Compilation compilation)
            {
                var derivedFrom = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
                var entities = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                var dbSet = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1");

                foreach (var type in AllTypesIn(compilation.Assembly.GlobalNamespace))
                {
                    for (var b = type.BaseType; b is not null; b = b.BaseType)
                    {
                        // First one wins: the reason names A derived type, and naming the first one the walk
                        // meets keeps the message stable across runs rather than whichever came last.
                        if (!derivedFrom.ContainsKey(b.OriginalDefinition))
                        {
                            derivedFrom.Add(b.OriginalDefinition, type.Name);
                        }
                    }

                    if (dbSet is null)
                    {
                        continue;
                    }

                    foreach (var member in type.GetMembers())
                    {
                        var memberType = member switch
                        {
                            IPropertySymbol property => property.Type,
                            IFieldSymbol field => field.Type,
                            IMethodSymbol method => method.ReturnType,
                            _ => null
                        };

                        if (memberType is INamedTypeSymbol { TypeArguments.Length: 1 } named &&
                            SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, dbSet) &&
                            named.TypeArguments[0] is INamedTypeSymbol entity)
                        {
                            entities.Add(entity.OriginalDefinition);
                        }
                    }
                }

                return new CompilationFacts(derivedFrom, entities);
            }

            /// <summary>The name of a type in this compilation that derives from <paramref name="type" />, or null.</summary>
            public string? DerivedTypeName(INamedTypeSymbol type)
            {
                return _derivedFrom.TryGetValue(type.OriginalDefinition, out var name) ? name : null;
            }

            /// <summary>True when <paramref name="type" /> appears as a <c>DbSet&lt;T&gt;</c> in this compilation.</summary>
            public bool IsEntity(INamedTypeSymbol type)
            {
                return _entities.Contains(type.OriginalDefinition);
            }

            /// <summary>Every named type declared in this assembly, walking nested types too.</summary>
            private static IEnumerable<INamedTypeSymbol> AllTypesIn(INamespaceOrTypeSymbol root)
            {
                foreach (var member in root.GetMembers())
                    switch (member)
                    {
                        case INamespaceSymbol ns:
                            foreach (var nested in AllTypesIn(ns)) yield return nested;

                            break;

                        case INamedTypeSymbol type:
                            yield return type;

                            foreach (var nested in AllTypesIn(type)) yield return nested;

                            break;
                    }
            }
        }

        /// <summary>
        ///     Classifies <paramref name="type" />, gathering the compilation-wide facts itself. Convenient for
        ///     a one-off question; a caller asking about many types should gather once with
        ///     <see cref="CompilationFacts.Gather" /> and use the overload below, because this sweeps the whole
        ///     assembly per call.
        /// </summary>
        public static Verdict Classify(INamedTypeSymbol type, Compilation compilation)
        {
            return Classify(type, compilation, CompilationFacts.Gather(compilation));
        }

        /// <summary>
        ///     Classifies <paramref name="type" /> against facts the caller already gathered — the shape a
        ///     per-pair caller uses. Pure: no diagnostic is reported, no state is touched, and the same
        ///     arguments always give the same verdict.
        /// </summary>
        public static Verdict Classify(INamedTypeSymbol type, Compilation compilation, CompilationFacts facts)
        {
            return Classify(type, compilation, facts, new List<INamedTypeSymbol>(), out _);
        }

        private static Verdict Classify(
            INamedTypeSymbol type,
            Compilation compilation,
            CompilationFacts facts,
            List<INamedTypeSymbol> path,
            out int alignment)
        {
            alignment = 1;

            // A struct cannot contain itself (CS0523), so an INLINE chain that returns to a type it is
            // already inside has no value form at all. The class the consumer wrote compiles, which is
            // exactly why the walk cannot lean on the language rule to stop it. Only inline edges reach
            // here â€” a collection element is a reference and is accepted in TryMeasureMember instead.
            if (Contains(path, type))
            {
                return Verdict.No(
                    $"'{type.Name}' contains itself by value, directly or through another transfer model, " +
                    "so it has no value layout");
            }

            if (path.Count >= MaxDepth)
            {
                return Verdict.No($"'{type.Name}' nests transfer models more than {MaxDepth} deep");
            }

            if (DeclarationRefusal(type, compilation, facts) is { } refused)
            {
                return refused;
            }

            if (BehaviourRefusal(type) is { } behaviour)
            {
                return behaviour;
            }

            // Layout is read from the FIELDS, including the auto-properties' backing fields — the same source
            // of truth LayoutHygiene uses for a struct, so a class and the struct it would become are measured
            // off the same list.
            var fields = new List<IFieldSymbol>();
            foreach (var member in type.GetMembers())
                if (member is IFieldSymbol { IsStatic: false } field)
                {
                    fields.Add(field);
                }

            // The same refusal LayoutHygiene makes for a struct, and it must be made here too: fields split
            // across partial declarations have no field order the compiler defines (CS0282), so the would-be
            // struct has no size — and a size is exactly what this returns. Accepting the shape would report a
            // number computed from whichever order the symbol walk happened to produce.
            if (BlittableProof.FieldsSpanPartialDeclarations(type, fields))
            {
                return Verdict.No(
                    $"'{type.Name}' declares instance fields in more than one partial declaration, so the " +
                    "compiler defines no field order for it (CS0282); keep every instance field in one declaration");
            }

            var members = new List<(string Name, int Size, int Align)>();
            var sizeIsUpperBound = false;

            path.Add(type);
            try
            {
                foreach (var field in fields)
                {
                    // An auto-property is named by its property, which is the name a consumer can type;
                    // `<Prop>k__BackingField` is not.
                    var name = field.AssociatedSymbol?.Name ?? field.Name;
                    if (!TryMeasureMember(
                            field.Type,
                            compilation,
                            facts,
                            path,
                            type.Name,
                            name,
                            out var measured,
                            out var estimated,
                            out var reason))
                    {
                        return Verdict.No(reason);
                    }

                    sizeIsUpperBound |= estimated;
                    members.Add((name, measured.Size, measured.Align));
                }
            }
            finally
            {
                path.RemoveAt(path.Count - 1);
            }

            if (members.Count == 0)
            {
                // A zero-byte struct is not the remedy for a class that carries nothing, and reporting it as
                // "0 B, eligible" would read as advice.
                return Verdict.No($"'{type.Name}' has no instance members to carry");
            }

            var layout = LayoutHygiene.LayOut(members);
            alignment = layout.Alignment;

            // What the derived-type sweep could actually see. A sealed type cannot be derived from at all, and
            // a non-public one can only be derived from inside the assembly the sweep walked; a public unsealed
            // one can be subclassed by a project this compilation never sees, so the verdict carries the scope
            // of its own evidence rather than letting a caller overstate it.
            var derivationCheckedWithinAssemblyOnly =
                type is { IsSealed: false, DeclaredAccessibility: Accessibility.Public };

            if (layout.Size > SuggestInSizeLimit)
            {
                return Verdict.Oversized(layout.Size, sizeIsUpperBound, derivationCheckedWithinAssemblyOnly);
            }

            return Verdict.Fits(
                layout.Size,
                layout.Size > SilentSizeLimit,
                sizeIsUpperBound,
                derivationCheckedWithinAssemblyOnly);
        }

        /// <summary>
        ///     Everything decidable from the DECLARATION — kind, inheritance, interfaces, ORM markers — or null
        ///     when none of it refuses. Ordered so the reason names what the consumer can see soonest: being
        ///     abstract is visible in the declaration, having a derived type somewhere is not.
        /// </summary>
        private static Verdict? DeclarationRefusal(
            INamedTypeSymbol type,
            Compilation compilation,
            CompilationFacts facts)
        {
            if (type.IsValueType)
            {
                return Verdict.No($"'{type.Name}' is already a value type");
            }

            if (type.TypeKind != TypeKind.Class)
            {
                return Verdict.No($"'{type.Name}' is not a class");
            }

            // A type imported from a referenced assembly is refused OUTRIGHT, and this guard has to come
            // before every rule that reads syntax, because those rules do not fail on it — they pass. A
            // metadata symbol has no DeclaringSyntaxReferences, so the constructor walk finds nothing to
            // object to and returns "allowed" for a constructor that validates; the derived-type sweep reads
            // this compilation's assembly and cannot see a subclass in the defining one; and its
            // auto-properties' backing fields are not imported under the default MetadataImportOptions, so the
            // member walk would call an ordinary auto-property "computed". Three rules silently vacuous and a
            // fourth actively wrong, on one root cause.
            //
            // The refusal is right on its own merits too: the code fix rewrites a DECLARATION, and there is no
            // declaration here to rewrite. Naming the assembly is what makes it actionable — the consumer has
            // to change that project, or nothing.
            if (type.DeclaringSyntaxReferences.IsEmpty)
            {
                var assembly = type.ContainingAssembly?.Name ?? "another assembly";
                return Verdict.No(
                    $"'{type.Name}' is declared in referenced assembly '{assembly}', not in source, so its " +
                    "shape cannot be checked and its declaration cannot be rewritten");
            }

            if (type.IsStatic)
            {
                return Verdict.No($"'{type.Name}' is a static class and holds no instance data");
            }

            if (type.IsAbstract)
            {
                return Verdict.No($"'{type.Name}' is abstract, and a struct cannot be a base type");
            }

            // An OPEN generic has no size: its members' widths depend on the type argument. Refused here so the
            // reason blames the declaration rather than blaming a type parameter for being unmeasurable.
            foreach (var argument in type.TypeArguments)
                if (argument.TypeKind == TypeKind.TypeParameter)
                {
                    return Verdict.No($"'{type.Name}' is generic; its size depends on the type argument");
                }

            if (type.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
            {
                return Verdict.No($"'{type.Name}' derives from '{baseType.Name}'");
            }

            if (!type.IsSealed && facts.DerivedTypeName(type) is { } derived)
            {
                return Verdict.No($"'{type.Name}' is not sealed and '{derived}' derives from it");
            }

            var equatable = compilation.GetTypeByMetadataName("System.IEquatable`1");
            foreach (var iface in type.AllInterfaces)
            {
                if (iface.SpecialType == SpecialType.System_IDisposable)
                {
                    // IDisposable is ownership with a lifetime. Copy the struct and the release is either
                    // duplicated or skipped — both silent, which is the failure class this project refuses.
                    return Verdict.No($"'{type.Name}' implements 'IDisposable', so its lifetime is owned");
                }

                // IEquatable<self> is the ONE permitted interface, because a readonly record struct
                // synthesises exactly that: nothing is lost in the rewrite. Every other interface is a boxing
                // conversion at each call, which puts back the allocation the whole change removes.
                if (equatable is not null &&
                    SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, equatable) &&
                    SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], type))
                {
                    continue;
                }

                return Verdict.No($"'{type.Name}' implements '{iface.Name}'");
            }

            return EntityRefusal(type, facts);
        }

        /// <summary>
        ///     The Entity Framework heuristic, and a deliberately blunt one: an ORM tracks entities BY
        ///     REFERENCE — change tracking, identity resolution and lazy loading all rest on it — so converting
        ///     one to a value type breaks the ORM rather than the code that would at least fail to compile.
        ///     Refusing a DTO wrongly here costs a hint; blessing an entity costs a consumer their data layer.
        ///     <para>
        ///         Matched by attribute NAME rather than by attribute type, so it fires without this generator
        ///         referencing EF Core or the annotation package at all, and catches a re-declared attribute in
        ///         either spelling.
        ///     </para>
        /// </summary>
        private static Verdict? EntityRefusal(INamedTypeSymbol type, CompilationFacts facts)
        {
            if (facts.IsEntity(type))
            {
                return Verdict.No(
                    $"'{type.Name}' is used as a 'DbSet<{type.Name}>' in this compilation, so it is an ORM entity");
            }

            if (EntityAttributeName(type.GetAttributes()) is { } onType)
            {
                return Verdict.No($"'{type.Name}' carries '[{onType}]', so it is an ORM entity");
            }

            foreach (var member in type.GetMembers())
                if (EntityAttributeName(member.GetAttributes()) is { } onMember)
                {
                    return Verdict.No(
                        $"'{type.Name}.{member.Name}' carries '[{onMember}]', so '{type.Name}' is an ORM entity");
                }

            return null;
        }

        private static string? EntityAttributeName(IEnumerable<AttributeData> attributes)
        {
            foreach (var attribute in attributes)
            {
                var name = attribute.AttributeClass?.Name;
                switch (name)
                {
                    case "Key":
                    case "KeyAttribute":
                    case "Table":
                    case "TableAttribute":
                    case "Owned":
                    case "OwnedAttribute":
                        return name;
                }
            }

            return null;
        }

        /// <summary>
        ///     Everything the type DOES rather than holds: methods, events, computed properties, constructors
        ///     with logic. Run before the layout walk so an event's hidden delegate field is never mistaken for
        ///     state, and so the reason names the member rather than its backing field.
        /// </summary>
        private static Verdict? BehaviourRefusal(INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers())
            {
                // What the COMPILER wrote is not behaviour the consumer chose. A record declares Equals,
                // GetHashCode, ToString, PrintMembers, EqualityContract, <Clone>$ and Deconstruct without
                // anyone typing them, and a rule that counted those would refuse every record ever written.
                if (member.IsImplicitlyDeclared || member.IsStatic)
                {
                    continue;
                }

                // Two modifiers a struct member simply may not carry, and the rewrite would turn each into a
                // compile error in the consumer's file rather than a diagnostic in ours: `protected` is CS0666
                // (a struct has no derived type to protect anything from) and `virtual` is CS0106 (there is
                // nothing to override it). A record's own protected virtual members â€” EqualityContract,
                // PrintMembers â€” are implicitly declared and never reach here.
                if (member.IsVirtual)
                {
                    return Verdict.No(
                        $"'{type.Name}.{member.Name}' is virtual, which a struct member cannot be (CS0106)");
                }

                if (member.DeclaredAccessibility is Accessibility.Protected
                    or Accessibility.ProtectedOrInternal or Accessibility.ProtectedAndInternal)
                {
                    return Verdict.No(
                        $"'{type.Name}.{member.Name}' is protected, which a struct member cannot be (CS0666)");
                }

                switch (member)
                {
                    case IEventSymbol:
                        // A subscription list belongs to an INSTANCE. Copy the struct and the subscribers
                        // travel with a copy nobody raises.
                        return Verdict.No($"'{type.Name}' declares the event '{member.Name}'");

                    case IPropertySymbol property when property.IsIndexer:
                        return Verdict.No($"'{type.Name}' declares an indexer");

                    case IPropertySymbol property when !HasBackingField(type, property):
                        // A computed property is a method wearing a property's clothes: no backing field, so
                        // no state and no layout.
                        return Verdict.No(
                            $"'{type.Name}.{property.Name}' is a computed property, not an auto-property");

                    case IMethodSymbol { MethodKind: MethodKind.Constructor } ctor
                        when ConstructorRefusal(type, ctor) is { } refused:
                        return refused;

                    case IMethodSymbol method when IsBehaviour(method):
                        return Verdict.No(
                            $"'{type.Name}' declares the method '{method.Name}'; a transfer model carries data only");
                }
            }

            return null;
        }

        /// <summary>
        ///     True when a method is behaviour rather than plumbing. Accessors belong to their property or
        ///     event, which are judged on their own; the equality-and-printing quartet is what a
        ///     <c>readonly record struct</c> would synthesise anyway, and a record permits a hand-written one
        ///     in its place, so those survive the rewrite intact.
        /// </summary>
        private static bool IsBehaviour(IMethodSymbol method)
        {
            switch (method.MethodKind)
            {
                case MethodKind.PropertyGet:
                case MethodKind.PropertySet:
                case MethodKind.EventAdd:
                case MethodKind.EventRemove:
                case MethodKind.EventRaise:
                case MethodKind.Constructor:
                    return false;
            }

            var isEqualityMember =
                (method.Name is "Equals" && method.Parameters.Length == 1) ||
                (method.Name is "GetHashCode" or "ToString" && method.Parameters.Length == 0);

            return !isEqualityMember;
        }

        private static bool HasBackingField(INamedTypeSymbol type, IPropertySymbol property)
        {
            foreach (var member in type.GetMembers())
                if (member is IFieldSymbol { AssociatedSymbol: { } associated } &&
                    SymbolEqualityComparer.Default.Equals(associated, property))
                {
                    return true;
                }

            return false;
        }

        /// <summary>
        ///     Refuses a constructor that does anything beyond assigning its own parameters to members.
        ///     <para>
        ///         Read from the BODY, never the signature: a constructor that validates its arguments is
        ///         indistinguishable from one that assigns them until you look inside. And the distinction
        ///         matters more for a struct than for a class, because <c>default(T)</c> and array allocation
        ///         reach a struct's fields without running any constructor at all — so validation that was an
        ///         invariant becomes a suggestion the moment the type is a value type.
        ///     </para>
        ///     <para>
        ///         A constructor with no <c>ConstructorDeclarationSyntax</c> is the compiler's own — a default
        ///         constructor or a positional record's primary constructor — and carries nothing to lose.
        ///     </para>
        /// </summary>
        private static Verdict? ConstructorRefusal(INamedTypeSymbol type, IMethodSymbol ctor)
        {
            foreach (var reference in ctor.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not ConstructorDeclarationSyntax syntax)
                {
                    continue;
                }

                if (syntax.Initializer is not null)
                {
                    return Verdict.No(
                        $"the constructor of '{type.Name}' chains to another constructor, so it carries logic");
                }

                var parameters = new HashSet<string>(StringComparer.Ordinal);
                foreach (var parameter in ctor.Parameters) parameters.Add(parameter.Name);

                if (syntax.ExpressionBody is not null)
                {
                    return IsParameterAssignment(syntax.ExpressionBody.Expression, parameters)
                        ? null
                        : Verdict.No($"the constructor of '{type.Name}' does more than assign its members");
                }

                if (syntax.Body is null)
                {
                    continue;
                }

                foreach (var statement in syntax.Body.Statements)
                    if (statement is not ExpressionStatementSyntax expression ||
                        !IsParameterAssignment(expression.Expression, parameters))
                    {
                        return Verdict.No($"the constructor of '{type.Name}' does more than assign its members");
                    }
            }

            return null;
        }

        /// <summary>Symbol-aware membership, so the two callers of the inline path agree on the comparer.</summary>
        private static bool Contains(List<INamedTypeSymbol> path, INamedTypeSymbol type)
        {
            foreach (var visiting in path)
                if (SymbolEqualityComparer.Default.Equals(visiting, type))
                {
                    return true;
                }

            return false;
        }

        /// <summary>True for <c>X = p</c> and <c>this.X = p</c>, where <c>p</c> names a parameter.</summary>
        private static bool IsParameterAssignment(ExpressionSyntax expression, HashSet<string> parameters)
        {
            if (expression is not AssignmentExpressionSyntax assignment ||
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                return false;
            }

            var target = assignment.Left switch
            {
                IdentifierNameSyntax identifier => identifier,
                MemberAccessExpressionSyntax
                {
                    Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name
                } => name,
                _ => null
            };

            return target is not null &&
                   assignment.Right is IdentifierNameSyntax source &&
                   parameters.Contains(source.Identifier.ValueText);
        }

        /// <summary>
        ///     What one member costs in the would-be struct, or false with a reason naming it. The three arms
        ///     are the spec's per-member verdict table: a reference leaf stays a reference, a nested transfer
        ///     model goes inline (optional ones wrapped in <c>Nullable&lt;T&gt;</c>), and everything else is
        ///     <see cref="LayoutHygiene" />'s to measure — with its refusal passed straight through, never
        ///     softened into a zero.
        /// </summary>
        private static bool TryMeasureMember(
            ITypeSymbol memberType,
            Compilation compilation,
            CompilationFacts facts,
            List<INamedTypeSymbol> path,
            string owner,
            string memberName,
            out (int Size, int Align) measured,
            out bool sizeIsUpperBound,
            out string reason)
        {
            measured = default;
            sizeIsUpperBound = false;
            reason = string.Empty;

            if (memberType.SpecialType == SpecialType.System_String)
            {
                measured = ReferenceField;
                sizeIsUpperBound = true;
                return true;
            }

            if (ElementTypeOfCollection(memberType, compilation) is { } element)
            {
                // The collection stays a reference field whatever its elements are — a struct cannot inline a
                // variable-length sequence. Its ELEMENT type is still checked, because the code fix rewrites
                // the reachable transfer-model subgraph, and an element it cannot rewrite is a member the
                // consumer must be told about rather than one silently left behind.
                //
                // An element already on the inline path is ACCEPTED here rather than refused as a cycle.
                // `Dto[] Children` inside `Dto` recurses through a REFERENCE: there is no layout recursion,
                // and `readonly record struct Dto(Dto[] Children)` is legal C#. Calling that a cycle told the
                // consumer something factually untrue. Every other rule for that type is being checked by its
                // own frame further up the stack, and reusing `path` — rather than starting a fresh one — is
                // what keeps the walk terminating on `A { B[] } / B { A[] }`.
                if (element is INamedTypeSymbol namedElement && Contains(path, namedElement))
                {
                    measured = ReferenceField;
                    sizeIsUpperBound = true;
                    return true;
                }

                if (!TryMeasureMember(
                        element,
                        compilation,
                        facts,
                        path,
                        owner,
                        memberName,
                        out _,
                        out _,
                        out reason))
                {
                    return false;
                }

                measured = ReferenceField;
                sizeIsUpperBound = true;
                return true;
            }

            if (memberType is INamedTypeSymbol { IsReferenceType: true } reference)
            {
                // IsShaped, not "Eligible": TooLarge is admitted DELIBERATELY, because it is a size band
                // and not a shape. A nested model over 64 B can still be inlined â€” it is a struct the
                // consumer would be told to pass by `in`, not one that cannot exist â€” and inlining it pushes
                // the OUTER type past 64 too, where the outer's own threshold reports it honestly. The rule
                // that an unshaped member refuses its owner is about a member that cannot become a struct at
                // all: an entity, a type with behaviour, a class with a base.
                var nested = Classify(reference, compilation, facts, path, out var nestedAlignment);
                if (!nested.IsShaped)
                {
                    reason =
                        $"member '{owner}.{memberName}' of type '{reference.Name}' is not transfer-model shaped: {nested.Reason}";
                    return false;
                }

                measured = (nested.Size, nestedAlignment);

                // An OPTIONAL nested model is Nullable<T> around the inline struct — a flag padded up to the
                // inner alignment, then the value. Sizing it as the bare inner would UNDER-count, and an
                // under-count is the one direction a bound may not err in.
                //
                // OBLIVIOUS counts as optional, and that is the point of testing for NotAnnotated rather than
                // for Annotated: where the nullable context is off, `Inner I` may legally hold null, so its
                // value form has to carry the flag. Only an explicit non-null annotation buys the bare inline
                // form. The error this way costs at most one alignment of over-count; the other way it reports
                // a struct smaller than the consumer will get.
                if (memberType.NullableAnnotation != NullableAnnotation.NotAnnotated)
                {
                    measured = LayoutHygiene.AsOptional(measured);
                }

                sizeIsUpperBound = nested.SizeIsUpperBound;
                return true;
            }

            if (LayoutHygiene.MeasureMember(memberType) is { } value)
            {
                measured = value;
                return true;
            }

            reason =
                $"member '{owner}.{memberName}' of type '{memberType.Name}' has no size this generator can prove";
            return false;
        }

        /// <summary>
        ///     The element type of an array or a <c>List&lt;T&gt;</c>, or null for anything else. Those two are
        ///     the collection shapes the plan permits in a transfer model; both are references in the would-be
        ///     struct, and both keep their own blit while costing the root's.
        /// </summary>
        private static ITypeSymbol? ElementTypeOfCollection(ITypeSymbol memberType, Compilation compilation)
        {
            if (memberType is IArrayTypeSymbol array)
            {
                return array.ElementType;
            }

            var list = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
            if (list is not null &&
                memberType is INamedTypeSymbol { TypeArguments.Length: 1 } named &&
                SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, list))
            {
                return named.TypeArguments[0];
            }

            return null;
        }
    }
}
