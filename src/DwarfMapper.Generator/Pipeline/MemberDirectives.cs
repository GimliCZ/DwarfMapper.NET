// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     One <c>[MapProperty]</c> or <c>[MapIgnore]</c> application written on a DTO MEMBER, parsed but not
    ///     judged.
    ///     <para>
    ///         <see cref="ArgumentCount" /> is carried rather than folded into a "this is the member form"
    ///         boolean on purpose: which arity is the member form differs between the two attributes
    ///         (<c>[MapProperty("Dest")]</c> takes one, bare <c>[MapIgnore]</c> takes none), and the two readers
    ///         that consume this do not judge a wrong arity identically — the registry front door has always
    ///         accepted <c>[MapIgnore("x")]</c> as a plain ignore, discarding the argument. Parsing and judging
    ///         are therefore kept apart, so a consumer that wants a different rule states it rather than
    ///         inheriting one.
    ///     </para>
    /// </summary>
    /// <param name="Ignore"><c>true</c> for <c>[MapIgnore]</c>, <c>false</c> for <c>[MapProperty]</c>.</param>
    /// <param name="ArgumentCount">Constructor arguments as written — the arity that decides the placement.</param>
    /// <param name="Name">
    ///     The single constructor argument, or <c>null</c> — when the arity is not one, but ALSO when it is one
    ///     and the argument is not a constant string. <c>[MapProperty(null)]</c> binds the string overload and is
    ///     at most <c>CS8625</c>, and a half-typed application leaves an error constant behind; a consumer that
    ///     reads arity as a proof of name hands a null downstream. Both consumers here check the name itself.
    /// </param>
    /// <param name="Use">The <c>Use =</c> converter name, or null.</param>
    /// <param name="When">The <c>When =</c> predicate name, or null.</param>
    /// <param name="HasNullSub">Whether <c>NullSubstitute =</c> was written at all (null is a legal value).</param>
    /// <param name="NullSub">The <c>NullSubstitute =</c> value; meaningful only when <paramref name="HasNullSub" />.</param>
    /// <param name="StringFormat">The <c>StringFormat =</c> format string, or null.</param>
    /// <param name="Loc">Where the attribute was written, for a diagnostic that refuses it.</param>
    internal sealed record MemberDirective(
        bool Ignore,
        int ArgumentCount,
        string? Name,
        string? Use,
        string? When,
        bool HasNullSub,
        TypedConstant NullSub,
        string? StringFormat,
        LocationInfo? Loc);

    /// <summary>
    ///     The one reader of the MEMBER-placement <c>[MapProperty]</c> / <c>[MapIgnore]</c> forms.
    ///     <para>
    ///         Shared by the two front doors where the annotated type declares its own mapping: the
    ///         <c>[MapTo]</c> registry (<see cref="Registry.MapToGenerator" />, where the annotated member is a
    ///         SOURCE member) and the co-located <c>[GenerateMap&lt;S,T&gt;]</c> host
    ///         (<see cref="MapperExtractor" />, where it is a DESTINATION member of the host). One reader rather
    ///         than two because a second copy is how the method and pair-scoped forms of <c>[MapNullSkip]</c>
    ///         ended up exact inverses of each other — two readers of one attribute form drift, and the drift is
    ///         invisible until something measures both.
    ///     </para>
    /// </summary>
    internal static class MemberDirectives
    {
        /// <summary>
        ///     A member's <c>[MapProperty]</c> / <c>[MapIgnore]</c> applications in source order. The i-th
        ///     directive binds to the i-th declared target — a <c>[MapTo]</c> target at the registry, a
        ///     <c>[GenerateMap]</c> pair at the co-located host — so the order is part of the meaning, not a
        ///     presentation detail.
        /// </summary>
        public static List<MemberDirective> Read(ISymbol member)
        {
            var ordered = new List<(string File, int Pos, MemberDirective Directive)>();
            foreach (var a in member.GetAttributes())
            {
                var cls = a.AttributeClass?.ToDisplayString();
                var isIgnore = cls == KnownNames.MapIgnoreFqn;
                if (!isIgnore && cls != KnownNames.MapPropertyFqn)
                {
                    continue;
                }

                var reference = a.ApplicationSyntaxReference;
                // Span.Start alone orders attributes only within ONE file. A partial property (C# 13) can carry
                // directives in two files, where the spans are independent offsets and the ordering — which decides
                // WHICH target each directive binds to — would depend on GetAttributes()' cross-file order.
                // Including the file path makes it total and stable across builds.
                var file = reference?.SyntaxTree.FilePath ?? string.Empty;
                var pos = reference?.Span.Start ?? 0;

                string? use = null;
                string? when = null;
                string? format = null;
                var hasNullSub = false;
                TypedConstant nullSub = default;
                foreach (var na in a.NamedArguments)
                    switch (na.Key)
                    {
                        case "Use" when na.Value.Value is string u:
                            use = u;
                            break;

                        case "When" when na.Value.Value is string w:
                            when = w;
                            break;

                        case "StringFormat" when na.Value.Value is string f:
                            format = f;
                            break;

                        case "NullSubstitute":
                            hasNullSub = true;
                            nullSub = na.Value;
                            break;
                    }

                var count = a.ConstructorArguments.Length;
                ordered.Add((file, pos, new MemberDirective(
                    isIgnore,
                    count,
                    count == 1 ? a.ConstructorArguments[0].Value as string : null,
                    use,
                    when,
                    hasNullSub,
                    nullSub,
                    format,
                    LocationInfo.From(reference?.GetSyntax().GetLocation() ?? Location.None))));
            }

            ordered.Sort((x, y) =>
            {
                var byFile = string.CompareOrdinal(x.File, y.File);
                return byFile != 0 ? byFile : x.Pos.CompareTo(y.Pos);
            });
            return ordered.ConvertAll(x => x.Directive);
        }
    }
}
