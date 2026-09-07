// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>What <see cref="ImmutabilityProof" /> could establish about a type. There is no fourth answer.</summary>
    internal enum ImmutabilityVerdict
    {
        /// <summary>
        ///     Nothing reachable from a value of this type can be mutated through any reference to it. Sharing the
        ///     reference instead of copying is observationally identical, so <c>[MapShare]</c> applies
        ///     automatically.
        /// </summary>
        Proven,

        /// <summary>
        ///     Not disproven, and not provable either: an interface, a non-sealed class, a type parameter, an
        ///     external shape whose members the proof cannot see through, or a reference cycle. The automatic path
        ///     copies (a refusal costs a copy); an explicit <c>[MapShare]</c> shares on the caller's assertion,
        ///     exactly as <c>[Reinterpret]</c> forces a blit the layout proof declines.
        /// </summary>
        Unprovable,

        /// <summary>
        ///     Provably mutable: some member reachable from this type is settable, so two graphs sharing one
        ///     instance could diverge from each other after the map. <c>[MapShare]</c> is REFUSED here — the
        ///     caller cannot assert away a fact the generator can see.
        /// </summary>
        Mutable
    }

    /// <summary>
    ///     Proves whether a value of a type can be handed to a second object graph <b>by reference</b> without
    ///     the two graphs becoming able to observe each other's changes — the precondition for
    ///     <c>[MapShare]</c>.
    ///     <para>
    ///         <b>The rule is the guarantee, not the declared interface.</b> <c>IReadOnlyList&lt;T&gt;</c> says
    ///         read-only and promises nothing: a <c>List&lt;T&gt;</c> assigned to it is still a
    ///         <c>List&lt;T&gt;</c> at run time and the source can mutate it after the map, silently coupling two
    ///         graphs the consumer believes are independent — and no diagnostic recovers from that afterwards.
    ///         So this proof answers <see cref="ImmutabilityVerdict.Proven" /> only for shapes that cannot be
    ///         mutated through ANY reference, and <see cref="ImmutabilityVerdict.Unprovable" /> for every
    ///         interface, every non-sealed class, and everything it cannot see through. A wrong refusal costs a
    ///         copy; a wrong acceptance costs correctness.
    ///     </para>
    ///     <para>
    ///         <b>It terminates, and a cycle refuses rather than loops.</b> The recursion carries the set of types
    ///         currently being judged, and re-entering one answers <see cref="ImmutabilityVerdict.Unprovable" />
    ///         immediately — the same register-before-build contract <see cref="NestedMappingRegistry" /> uses to
    ///         keep a recursive DTO from spinning the generator, applied to a proof instead of to a build queue.
    ///         A breadth cap (<see cref="MaxTypesVisited" />) bounds the fan-out the same way that registry's
    ///         <c>MaxPairs</c> does.
    ///     </para>
    ///     <para>
    ///         <b>Why the immutable-collection family is a name list rather than a shape check.</b>
    ///         <c>ImmutableArray&lt;T&gt;</c> holds its backing array in a field that is deliberately NOT
    ///         <c>readonly</c> (<c>ImmutableInterlocked</c> writes through a <c>ref</c> to it), and
    ///         <c>string</c> writes its own fields through unsafe code. The general member rule below therefore
    ///         refuses both, correctly for a rule that can only read declarations — so the types whose
    ///         immutability is a documented guarantee of the platform are named, and their type ARGUMENTS are
    ///         still proven recursively. Nothing else is granted that trust.
    ///     </para>
    /// </summary>
    internal static class ImmutabilityProof
    {
        /// <summary>
        ///     Fan-out cap for one proof. Bounds the work a pathological graph can ask of the generator, the way
        ///     <c>NestedMappingRegistry.MaxPairs</c> bounds its build queue. Exceeding it is
        ///     <see cref="ImmutabilityVerdict.Unprovable" />, never <see cref="ImmutabilityVerdict.Proven" />:
        ///     running out of budget is not evidence of anything.
        /// </summary>
        private const int MaxTypesVisited = 512;

        /// <summary>
        ///     The member's own name and nothing else, keyword-escaped. See <see cref="Display" /> for why a
        ///     built-in format is not used here.
        /// </summary>
        private static readonly SymbolDisplayFormat BareMemberName = new(
            memberOptions: SymbolDisplayMemberOptions.None,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

        /// <summary>
        ///     The platform types whose deep immutability is a documented guarantee rather than something a
        ///     declaration scan can establish. Matched on the ORIGINAL definition's fully-qualified name, so a
        ///     consumer type that merely shares a simple name is not admitted.
        /// </summary>
        private static readonly HashSet<string> KnownImmutable = new(StringComparer.Ordinal)
        {
            "System.String",
            "System.Version",

            // System.Type is here for a reason worth stating: every `record` declares a `protected virtual Type
            // EqualityContract { get; }`, so without it EVERY record in existence would be Unprovable through a
            // member the consumer never wrote. Runtime Type instances are per-type singletons the CLR owns and
            // nothing in user code can mutate, so sharing a reference to one aliases nothing.
            //
            // System.Uri is deliberately NOT here despite being documented immutable: it is not sealed, so a
            // derived Uri may add settable state, and this proof does not accept "documented" over "enforced".
            "System.Type",
            "System.Collections.Immutable.ImmutableArray<T>",
            "System.Collections.Immutable.ImmutableList<T>",
            "System.Collections.Immutable.ImmutableHashSet<T>",
            "System.Collections.Immutable.ImmutableSortedSet<T>",
            "System.Collections.Immutable.ImmutableQueue<T>",
            "System.Collections.Immutable.ImmutableStack<T>",
            "System.Collections.Immutable.ImmutableDictionary<TKey, TValue>",
            "System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>"
        };

        /// <summary>
        ///     The read-only sequence interfaces, for which <c>Array.Empty&lt;T&gt;()</c> is an allocation-free
        ///     stand-in for the empty collection the copying helper would have built. Read-only in NAME only —
        ///     see the class remarks; this list exists for the empty VALUE, never as evidence of immutability.
        /// </summary>
        private static readonly HashSet<string> ReadOnlySequenceInterfaces = new(StringComparer.Ordinal)
        {
            "System.Collections.Generic.IEnumerable<T>",
            "System.Collections.Generic.IReadOnlyCollection<T>",
            "System.Collections.Generic.IReadOnlyList<T>"
        };

        /// <summary>
        ///     Classifies <paramref name="type" />. <paramref name="reason" /> is a member-path explanation for
        ///     <see cref="ImmutabilityVerdict.Mutable" /> and <see cref="ImmutabilityVerdict.Unprovable" />, empty
        ///     for <see cref="ImmutabilityVerdict.Proven" />.
        /// </summary>
        public static ImmutabilityVerdict Classify(ITypeSymbol type, out string reason)
        {
            var visiting = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var budget = MaxTypesVisited;
            return Classify(type, visiting, ref budget, out reason);
        }

        /// <summary>
        ///     An allocation-free expression producing the EMPTY value of <paramref name="type" />, or null when
        ///     none exists.
        ///     <para>
        ///         The share replaces a helper whose null arm returns an empty collection (the
        ///         <c>NullCollectionStrategy.AsEmpty</c> default), so a bare assignment would change what a null —
        ///         or, for <c>ImmutableArray&lt;T&gt;</c>, a <c>default</c> — source produces. The guard keeps that
        ///         arm's answer without keeping the helper, and costs nothing at run time: every value it names is
        ///         a cached singleton.
        ///     </para>
        ///     <para>
        ///         Deliberately NOT a general "call the parameterless constructor" fallback. A share that has to
        ///         allocate its empty case is a share whose worst case is the copy it replaced, and a type with no
        ///         canonical empty has no answer this method may invent.
        ///     </para>
        /// </summary>
        public static string? TryEmptyExpression(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named)
            {
                return null;
            }

            var definition = named.OriginalDefinition.ToDisplayString();
            if (ReadOnlySequenceInterfaces.Contains(definition) && named.TypeArguments.Length == 1)
            {
                return "global::System.Array.Empty<" + Fqn(named.TypeArguments[0]) + ">()";
            }

            // The `Empty` convention: a static, public, get-able member of the type's own constructed type.
            // ImmutableArray/List/HashSet/Dictionary/Queue/Stack all follow it, and so does any consumer type
            // that chose to; asking the symbol is what lets the second case work without a second name list.
            foreach (var member in named.GetMembers("Empty"))
            {
                if (!member.IsStatic || member.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                var memberType = member switch
                {
                    IPropertySymbol { GetMethod: not null } p => p.Type,
                    IFieldSymbol f => f.Type,
                    _ => null
                };

                if (memberType is not null && SymbolEqualityComparer.Default.Equals(memberType, named))
                {
                    return Fqn(named) + ".Empty";
                }
            }

            return null;
        }

        /// <summary>
        ///     True when a value of <paramref name="type" /> can be <c>default</c> in a way the copying helper
        ///     treated as "no collection" — the <c>ImmutableArray&lt;T&gt;</c> case, where the struct is never
        ///     null yet wraps a null array, so the guard has to test <c>IsDefault</c> rather than <c>is null</c>.
        /// </summary>
        public static bool GuardsOnDefault(ITypeSymbol type)
        {
            return type is INamedTypeSymbol named &&
                   named.OriginalDefinition.ToDisplayString() == "System.Collections.Immutable.ImmutableArray<T>";
        }

        private static string Fqn(ITypeSymbol type)
        {
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private static ImmutabilityVerdict Classify(
            ITypeSymbol type,
            HashSet<ISymbol> visiting,
            ref int budget,
            out string reason)
        {
            reason = "";

            // An unmanaged type holds no managed reference at all, at any depth: there is nothing for a second
            // graph to reach. This subsumes every primitive, every enum, Guid/DateTime/TimeSpan and any consumer
            // struct built from them, and it is a property the compiler computes rather than one this file
            // restates as a name list that could drift.
            if (type.IsUnmanagedType && type.TypeKind is not (TypeKind.Pointer or TypeKind.FunctionPointer))
            {
                return ImmutabilityVerdict.Proven;
            }

            switch (type.TypeKind)
            {
                case TypeKind.Pointer:
                case TypeKind.FunctionPointer:
                case TypeKind.Dynamic:
                case TypeKind.Delegate:
                case TypeKind.TypeParameter:
                case TypeKind.Error:
                    reason = $"'{type.ToDisplayString()}' is a shape the immutability proof cannot see through";
                    return ImmutabilityVerdict.Unprovable;

                case TypeKind.Array:
                    // An array's elements are settable through any reference to it. This is a DISPROOF, not a
                    // gap: T[] is the canonical shared-mutable-state shape.
                    reason = $"'{type.ToDisplayString()}' is an array, whose elements are settable through any reference to it";
                    return ImmutabilityVerdict.Mutable;
            }

            if (type is not INamedTypeSymbol named)
            {
                reason = $"'{type.ToDisplayString()}' is a shape the immutability proof cannot see through";
                return ImmutabilityVerdict.Unprovable;
            }

            if (budget-- <= 0)
            {
                reason = "the immutability proof ran out of budget before it could finish";
                return ImmutabilityVerdict.Unprovable;
            }

            // Re-entry means a reference cycle. Answer Unprovable and unwind rather than recursing forever --
            // the brief's non-negotiable: terminate, and refuse on a cycle.
            //
            // Keyed on the CONSTRUCTED type, not the original definition. A true cycle recurs at the identical
            // constructed symbol (a Node whose Next is a Node), so termination is unaffected; keying on the
            // definition additionally refused ImmutableList<ImmutableList<int>>, where the inner instantiation
            // is a DIFFERENT type that merely shares a generic definition -- reported as a "reference cycle",
            // which was a false statement about a perfectly ordinary nested collection. Unbounded instantiation
            // (a Node<Node<T>> chain) is what MaxTypesVisited is for, and it still catches it.
            if (!visiting.Add(named))
            {
                reason = $"'{type.ToDisplayString()}' takes part in a reference cycle, which the proof refuses rather than following";
                return ImmutabilityVerdict.Unprovable;
            }

            try
            {
                return ClassifyNamed(named, visiting, ref budget, out reason);
            }
            finally
            {
                visiting.Remove(named);
            }
        }

        private static ImmutabilityVerdict ClassifyNamed(
            INamedTypeSymbol named,
            HashSet<ISymbol> visiting,
            ref int budget,
            out string reason)
        {
            reason = "";

            // Nullable<T> adds a bool beside a T and no way to write either after construction.
            if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return Classify(named.TypeArguments[0], visiting, ref budget, out reason);
            }

            if (KnownImmutable.Contains(named.OriginalDefinition.ToDisplayString()))
            {
                return ClassifyTypeArguments(named, visiting, ref budget, out reason);
            }

            if (named.TypeKind == TypeKind.Interface)
            {
                // The type ARGUMENTS are judged even though the interface itself cannot be: an
                // IReadOnlyList<Mutable> is disproven by its element, and letting the interface's own verdict
                // short-circuit would hand [MapShare] a shape the proof had already refuted one level down.
                // Unprovable is a gap the caller may assert past; Mutable is a fact, and it survives the gap.
                if (ClassifyTypeArguments(named, visiting, ref budget, out var argReason) == ImmutabilityVerdict.Mutable)
                {
                    reason = argReason;
                    return ImmutabilityVerdict.Mutable;
                }

                // The ruling this feature is built on: an interface is not a promise. Whatever its members
                // declare, the INSTANCE behind it is some other type entirely, and that type is what a source
                // can mutate after the map.
                reason = $"'{named.ToDisplayString()}' is an interface: the instance behind it may be a mutable type";
                return ImmutabilityVerdict.Unprovable;
            }

            // A member declared on a base or found on a derived type is reachable through the same reference,
            // so the walk covers the whole chain -- and an unsealed class has no knowable "whole chain".
            var mutable = ClassifyMembers(named, visiting, ref budget, out var memberReason);
            if (mutable == ImmutabilityVerdict.Mutable)
            {
                reason = memberReason;
                return ImmutabilityVerdict.Mutable;
            }

            if (named.IsReferenceType && !named.IsSealed)
            {
                reason = $"'{named.ToDisplayString()}' is not sealed: a derived instance may add settable state";
                return ImmutabilityVerdict.Unprovable;
            }

            if (mutable == ImmutabilityVerdict.Unprovable)
            {
                reason = memberReason;
            }

            return mutable;
        }

        private static ImmutabilityVerdict ClassifyTypeArguments(
            INamedTypeSymbol named,
            HashSet<ISymbol> visiting,
            ref int budget,
            out string reason)
        {
            reason = "";
            var worst = ImmutabilityVerdict.Proven;
            foreach (var arg in named.TypeArguments)
            {
                var verdict = Classify(arg, visiting, ref budget, out var argReason);
                if (verdict == ImmutabilityVerdict.Mutable)
                {
                    reason = argReason;
                    return ImmutabilityVerdict.Mutable;
                }

                if (verdict == ImmutabilityVerdict.Unprovable && worst == ImmutabilityVerdict.Proven)
                {
                    worst = ImmutabilityVerdict.Unprovable;
                    reason = argReason;
                }
            }

            return worst;
        }

        /// <summary>
        ///     Walks the instance state a value of <paramref name="named" /> exposes, base chain included, and
        ///     reports the WORST verdict any of it earns. A settable member short-circuits: one is a disproof.
        /// </summary>
        private static ImmutabilityVerdict ClassifyMembers(
            INamedTypeSymbol named,
            HashSet<ISymbol> visiting,
            ref int budget,
            out string reason)
        {
            reason = "";
            var worst = ImmutabilityVerdict.Proven;

            for (var type = named; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
            foreach (var member in type.GetMembers())
            {
                if (member.IsStatic)
                {
                    // Static state is shared by every instance already; sharing an instance neither creates nor
                    // widens that. It is not this proof's business.
                    continue;
                }

                switch (member)
                {
                    case IEventSymbol:
                        // An event's delegate field is written by every subscriber, and the delegate itself holds
                        // references to whatever its targets close over.
                        reason = $"'{Display(named, member)}' is an event, whose invocation list is mutable from anywhere";
                        return ImmutabilityVerdict.Mutable;

                    case IPropertySymbol { SetMethod: { IsInitOnly: false } } p:
                        reason = $"'{Display(named, p)}' has a setter, so sharing would alias mutable state";
                        return ImmutabilityVerdict.Mutable;

                    case IPropertySymbol p:
                    {
                        var verdict = Classify(p.Type, visiting, ref budget, out var memberReason);
                        if (verdict == ImmutabilityVerdict.Mutable)
                        {
                            reason = memberReason;
                            return ImmutabilityVerdict.Mutable;
                        }

                        if (verdict == ImmutabilityVerdict.Unprovable && worst == ImmutabilityVerdict.Proven)
                        {
                            worst = ImmutabilityVerdict.Unprovable;
                            reason = memberReason;
                        }

                        break;
                    }

                    case IFieldSymbol f:
                    {
                        // A property's or event's backing field is judged through the property/event itself: an
                        // `init` auto-property has a backing field the compiler does NOT mark readonly, so
                        // reading the field here would refuse exactly the shape this feature exists to accept.
                        if (f.AssociatedSymbol is not null)
                        {
                            break;
                        }

                        if (!f.IsReadOnly && !f.IsConst)
                        {
                            reason = $"'{Display(named, f)}' is a writable field, so sharing would alias mutable state";
                            return ImmutabilityVerdict.Mutable;
                        }

                        var verdict = Classify(f.Type, visiting, ref budget, out var memberReason);
                        if (verdict == ImmutabilityVerdict.Mutable)
                        {
                            reason = memberReason;
                            return ImmutabilityVerdict.Mutable;
                        }

                        if (verdict == ImmutabilityVerdict.Unprovable && worst == ImmutabilityVerdict.Proven)
                        {
                            worst = ImmutabilityVerdict.Unprovable;
                            reason = memberReason;
                        }

                        break;
                    }
                }
            }

            return worst;
        }

        /// <summary>
        ///     <c>Type.Member</c> for a diagnostic message. <see cref="ISymbol.ToDisplayString(SymbolDisplayFormat)" />
        ///     rather than <see cref="ISymbol.Name" />: the name a consumer wrote as <c>@class</c> keeps its
        ///     <c>@</c> here, and this string is READ by a human, never re-parsed as an identifier.
        ///     <para>
        ///         The format is spelled out rather than borrowed. <c>MinimallyQualifiedFormat</c> prepends the
        ///         member's TYPE and re-qualifies its container, so a property came out as
        ///         <c>Demo.Loose.string Loose.Name</c> — the owner named twice with a type wedged between. The
        ///         owner is already on the left of the dot; all this needs from the member is its name.
        ///     </para>
        /// </summary>
        private static string Display(INamedTypeSymbol owner, ISymbol member)
        {
            return owner.ToDisplayString() + "." + member.ToDisplayString(BareMemberName);
        }
    }
}
