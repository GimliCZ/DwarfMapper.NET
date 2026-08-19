// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

internal static partial class MapperExtractor
{
    /// <summary>
    ///     The member-level configuration one <c>[GenerateMap&lt;S,T&gt;]</c> pair gathers from the members of
    ///     its co-located host. Shaped as exactly what the pair's <c>ResolveMembers</c> call already takes, so
    ///     the host form joins the pair-scoped <c>[MapProperty&lt;S,T&gt;]</c> form at the same seam instead of
    ///     acquiring a code path of its own.
    /// </summary>
    private sealed class HostPairDirectives
    {
        public readonly List<(string Target, bool HasNullSub, TypedConstant NullSub, string? When,
            string? NullSubLiteral)> Extras = new();

        public readonly List<(string Source, string Target, string? Use)> Explicit = new();
        public readonly List<string> Ignores = new();
        public readonly Dictionary<string, string> StringFormats = new(StringComparer.Ordinal);
    }

    /// <summary>No host directives at all — every shape that is not a co-located host.</summary>
    private static readonly Dictionary<int, HostPairDirectives> EmptyHostDirectives = new();

    /// <summary>
    ///     Reads the MEMBER-placement <c>[MapProperty]</c> / <c>[MapIgnore]</c> forms off a co-located
    ///     <c>[GenerateMap&lt;S,T&gt;]</c> host, keyed by the index of the pair each directive binds to.
    ///     <para>
    ///         At this endpoint the mapping is declared BY the annotated type, so a member of it is part of
    ///         the declaration — which is the whole reason the member form means anything here and means
    ///         nothing on a <c>[DwarfMapper]</c> class, where the DTO pair is two ordinary types the consumer
    ///         may not even own. The annotated member is the DESTINATION, so the one argument names the SOURCE
    ///         member it is filled from; that is the mirror of the <c>[MapTo]</c> registry, where the annotated
    ///         type is the source and the argument names the destination. Both are read by the one
    ///         <see cref="MemberDirectives" /> parser, so the two placements cannot drift apart.
    ///     </para>
    ///     <para>
    ///         Confined to pairs whose TARGET is the host, and to the host's OWN members. A pair the host
    ///         merely declares about two other types is not configured by the host's members, and a member
    ///         inherited from a base type belongs to that base type's declaration, not to this one. Anything
    ///         the rule cannot place is refused as <c>DWARF089</c> rather than dropped — being dropped in
    ///         silence is the defect this reader exists to end (surface-matrix finding D20).
    ///     </para>
    /// </summary>
    /// <param name="host">The annotated class; also the destination type of the pairs this configures.</param>
    /// <param name="declaredPairs">
    ///     The <c>[GenerateMap&lt;S,T&gt;]</c> pairs as declared, BEFORE wrapper expansion appends synthetic
    ///     ones — the returned keys index into that same list.
    /// </param>
    private static Dictionary<int, HostPairDirectives> ReadCoLocatedHostDirectives(
        INamedTypeSymbol host, List<(ITypeSymbol Src, INamedTypeSymbol Tgt)> declaredPairs,
        List<DiagnosticInfo> diagnostics)
    {
        // The pairs a host member can speak about, in declaration order: a stacked directive binds to the
        // i-th of THESE, not to the i-th [GenerateMap] overall, so a host that also declares an unrelated
        // pair does not shift its own members' bindings.
        var hostTargeted = new List<int>();
        for (var i = 0; i < declaredPairs.Count; i++)
            if (SymbolEqualityComparer.Default.Equals(declaredPairs[i].Tgt, host))
                hostTargeted.Add(i);

        var byPair = new Dictionary<int, HostPairDirectives>();

        foreach (var member in host.GetMembers())
        {
            if (member is not IPropertySymbol { IsIndexer: false }
                && member is not IFieldSymbol { IsImplicitlyDeclared: false })
                continue;

            var directives = MemberDirectives.Read(member);
            if (directives.Count == 0) continue;
            if (!DirectivesArePlaceable(host, member, directives, hostTargeted.Count, diagnostics)) continue;

            for (var k = 0; k < hostTargeted.Count; k++)
            {
                var d = directives.Count == 1 ? directives[0] : directives[k];
                if (!byPair.TryGetValue(hostTargeted[k], out var config))
                {
                    config = new HostPairDirectives();
                    byPair[hostTargeted[k]] = config;
                }

                if (d.Ignore)
                {
                    config.Ignores.Add(member.Name);
                    continue;
                }

                // `!` is carried by DirectivesArePlaceable, which drops the member's whole directive SET
                // unless every non-ignore one names something — arity one does not imply a name, because
                // [MapProperty(null)] is a legal application and a half-typed one leaves an error constant.
                config.Explicit.Add((d.Name!, member.Name, d.Use));
                if (d.HasNullSub || d.When is not null)
                    config.Extras.Add((member.Name, d.HasNullSub, d.NullSub, d.When, null));
                if (d.StringFormat is not null)
                    config.StringFormats[member.Name] = d.StringFormat;
            }
        }

        return byPair;
    }

    /// <summary>
    ///     Whether every directive on one host member can be placed, reporting <c>DWARF089</c> for each that
    ///     cannot. Returns <c>false</c> when the member's directives are dropped as a set — a member whose
    ///     bindings are half-applied would emit a mapping nobody wrote, which is worse than the one they did.
    /// </summary>
    /// <param name="hostTargetedPairs">How many declared pairs have the host as their destination.</param>
    private static bool DirectivesArePlaceable(INamedTypeSymbol host, ISymbol member,
        List<MemberDirective> directives, int hostTargetedPairs, List<DiagnosticInfo> diagnostics)
    {
        var placeable = true;

        foreach (var d in directives)
        {
            // Which arity IS the member form differs per attribute: [MapProperty("Source")] takes one
            // argument, a bare [MapIgnore] takes none. Keyed on "not the member form's arity" rather than on
            // the specific wrong one, so a constructor added later is refused here by default instead of
            // being accepted and discarded — the same rule, and for the same reason, as the registry's.
            if (!d.Ignore && d.ArgumentCount != 1)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.MisplacedDirectiveOnCoLocatedHostMember, d.Loc,
                    $"[MapProperty] on '{member.Name}' of the co-located host '{host.Name}' was written with "
                    + $"{d.ArgumentCount} arguments — that is the METHOD-placement overload, which names a "
                    + "source AND a destination because a mapping method has no annotated member to be one. "
                    + $"On a host member the destination IS '{member.Name}', so name only the source it is "
                    + $"filled from: [MapProperty(\"<source>\")]."));
                placeable = false;
            }
            // Arity one does NOT imply a name. [MapProperty(null)] binds the string overload and is at most
            // CS8625; a half-typed application in the IDE leaves an error constant in its place, and the
            // generator runs on every keystroke. Either way `Name` is null here, and a null source name
            // reaches member resolution as a name that does not exist — which threw, surfacing as CS8785
            // against a file the consumer never wrote. Refused as unplaceable instead: BEFORE this reader
            // existed the shape was ignored entirely, and a generator that fails on input it used to ignore
            // is worse than the gap it was closing. Every sibling path guards the same thing —
            // MapToGenerator's `!string.IsNullOrEmpty`, ReadExplicitMaps' `is string`, and DWARF088's own
            // `as string ?? "…"`, whose comment names this hazard.
            else if (!d.Ignore && string.IsNullOrEmpty(d.Name))
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.MisplacedDirectiveOnCoLocatedHostMember, d.Loc,
                    $"[MapProperty] on '{member.Name}' of the co-located host '{host.Name}' names no source "
                    + "member — the argument is null, empty, or not a constant string. The member form's one "
                    + $"argument is the SOURCE member '{member.Name}' is filled from, so there is nothing "
                    + "here to bind to: [MapProperty(\"<source>\")]."));
                placeable = false;
            }
            else if (d.Ignore && d.ArgumentCount != 0)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.MisplacedDirectiveOnCoLocatedHostMember, d.Loc,
                    $"[MapIgnore(\"{d.Name ?? "…"}\")] on '{member.Name}' of the co-located host "
                    + $"'{host.Name}' names a destination to exclude — that is the form for a mapping method "
                    + $"or a mapper class. On a host member THE ANNOTATED MEMBER is what gets excluded, so it "
                    + $"names nothing further: write a bare [MapIgnore] to exclude '{member.Name}', or move "
                    + $"[MapIgnore(\"{d.Name ?? "…"}\")] onto '{host.Name}' itself, where the named form is "
                    + "read."));
                placeable = false;
            }
        }

        if (!placeable) return false;

        if (hostTargetedPairs == 0)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.MisplacedDirectiveOnCoLocatedHostMember, directives[0].Loc,
                $"'{member.Name}' carries a member-placement [MapProperty]/[MapIgnore], which says how THE "
                + $"ANNOTATED MEMBER is mapped — but '{host.Name}' is not the destination of any "
                + "[GenerateMap<,>] pair declared on it, so there is no mapping for the member to configure. "
                + "Move the directive onto the destination type, or configure the pair from this class with "
                + "[MapProperty<TSource, TTarget>(\"source\", \"target\")] / [MapIgnore<TTarget>(\"member\")]."));
            return false;
        }

        if (directives.Count > 1 && directives.Count != hostTargetedPairs)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.MisplacedDirectiveOnCoLocatedHostMember, directives[0].Loc,
                $"'{member.Name}' carries {directives.Count} member-placement directives, but '{host.Name}' "
                + $"is the destination of {hostTargetedPairs} [GenerateMap<,>] pair"
                + (hostTargetedPairs == 1 ? "" : "s") + " declared on it. Stacked directives bind "
                + "POSITIONALLY, one per pair in source order — write a single directive, which applies to "
                + "every pair, or exactly one per pair."));
            return false;
        }

        return true;
    }
}
