// SPDX-License-Identifier: GPL-2.0-only

using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Core
{
    /// <summary>
    ///     Turns a member name into something that can be written into emitted C#.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A DTO with a member called <c>@class</c> or <c>@event</c> is ordinary in code generated from a JSON
    ///         or OpenAPI schema, and <see cref="Microsoft.CodeAnalysis.ISymbol.Name" /> hands it over WITHOUT the
    ///         <c>@</c> — the escape is syntax, not part of the name. Written straight into an object initializer
    ///         that produced <c>class = src.class,</c>, which the compiler parses as a malformed event declaration:
    ///         <c>CS0065</c>/<c>CS0101</c>/<c>CS0102</c>, with an EMPTY member name, reported against generated
    ///         code the consumer never wrote and with no DwarfMapper diagnostic to connect it to anything.
    ///     </para>
    ///     <para>
    ///         Found by the R18-29 shape harvest on its first run — a shape neither this corpus nor Mapperly's had
    ///         ever asked about, which is exactly the argument for harvesting from outside.
    ///     </para>
    ///     <para>
    ///         Only RESERVED keywords are escaped. A contextual keyword (<c>value</c>, <c>record</c>, <c>from</c>)
    ///         is a perfectly good identifier already, and prefixing it would churn every existing golden file for
    ///         no gain.
    ///     </para>
    /// </remarks>
    internal static class Identifiers
    {
        /// <summary>The name as it must appear in emitted code — <c>class</c> becomes <c>@class</c>.</summary>
        public static string Escape(string name)
        {
            if (name.Length == 0 || name[0] == '@')
            {
                return name;
            }

            return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
        }

        /// <summary>
        ///     The same, for a name written in a TYPE-DECLARATION position — <c>record</c> becomes
        ///     <c>@record</c> as well as <c>class</c> becoming <c>@class</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A separate helper rather than a widening of <see cref="Escape" />, because the two positions
        ///         genuinely differ and the difference is measured, not assumed. As a MEMBER name a contextual
        ///         keyword needs nothing — <c>public int record { get; }</c> is fine — which is why
        ///         <see cref="Escape" /> deliberately leaves it alone and why widening it would churn every
        ///         golden file for no gain. As a TYPE name it is not fine: of the 46 contextual keywords
        ///         Roslyn knows, SIX break one — <c>extension</c>, <c>file</c>, <c>partial</c>,
        ///         <c>record</c>, <c>required</c> and <c>scoped</c> — and they do not break it the same way,
        ///         which is the part worth writing down. <c>extension</c> (<c>CS9306</c>), <c>file</c>
        ///         (<c>CS9056</c>), <c>required</c> (<c>CS9029</c>) and <c>scoped</c> (<c>CS9062</c>) are
        ///         refused outright wherever the type is DECLARED. <c>record</c> and <c>partial</c> declare
        ///         perfectly well — <c>readonly ref struct record { }</c> compiles — and break only where the
        ///         name is READ as a modifier of what follows it, a return type (<c>public record Make()</c>
        ///         parses as a positional record) being the usual site. So a check that looked only at the
        ///         declaration would have called two of the six safe.
        ///         <para>
        ///             Re-measured 2026-09-07 against the live compiler, and it corrected the note it
        ///             replaces twice over: that note said "of 29 contextual keywords, five broke", and both
        ///             numbers were wrong — <c>extension</c> arrived with C# 14 and no hand-written list
        ///             could have known. The sweep and the per-keyword codes are pinned in
        ///             <c>tests/DwarfMapper.Generator.Tests/Core/IdentifiersTests.cs</c>, which enumerates
        ///             <c>SyntaxFacts.GetContextualKeywordKinds()</c> rather than restating a list, so the
        ///             next keyword the language adds fails a test instead of a consumer's build.
        ///         </para>
        ///     </para>
        ///     <para>
        ///         Escaping rather than refusing, which is the whole point: <c>@</c> is C#'s own mechanism for
        ///         using a keyword as an identifier, so there is nothing to refuse and no list to keep. It also
        ///         dissolves the one case no syntactic check could catch — an unescaped <c>scoped</c> parses
        ///         into the same tree as a good name and fails later in the binder, while <c>@scoped</c> cannot
        ///         be read as a modifier at all, so the ambiguity has nothing to bind. The <c>@</c> is syntax:
        ///         the type's name is still <c>scoped</c>, and a consumer may write it either way.
        ///     </para>
        ///     <para>
        ///         Its callers are <see cref="DwarfMapper.Generator.Model.MapperClassModel.EmitClassName" />, its
        ///         containing-type chain, and the fully-qualified name built from both — every type-DECLARATION
        ///         position the generator writes. It reached them a day after it was kept with no caller at all:
        ///         its only one had been the <c>[GenerateView]</c> endpoint, withdrawn 2026-09-07 (see
        ///         <c>Issues/round29/WITHDRAWN-generated-views.md</c>), and it was retained on the argument that
        ///         nothing about it was view-specific. That argument held. A consumer may legally write
        ///         <c>public partial class @record</c>, <c>ISymbol.Name</c> hands back <c>record</c>, and the
        ///         emitted <c>public partial class record</c> is <c>CS8860</c> — a WARNING, unsuppressible from
        ///         a <c>.g.cs</c>, which an errors-only probe called green
        ///         (<c>ConsumerNamedPositionsCompileTests.P16</c> measures exactly that).
        ///     </para>
        /// </remarks>
        public static string EscapeTypeName(string name)
        {
            if (name.Length == 0 || name[0] == '@')
            {
                return name;
            }

            return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None &&
                   SyntaxFacts.GetContextualKeywordKind(name) == SyntaxKind.None
                ? name
                : "@" + name;
        }

        /// <summary>
        ///     The name with any leading <c>@</c> removed — for a name the generator is about to COMPOSE a new
        ///     identifier out of, rather than emit whole.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The opposite direction from <see cref="Escape" />, and it exists because applying the wrong one
        ///         of the two is how this class of defect kept recurring. The convenience facade builds its
        ///         extension method as <c>"To" + ShortName(returnType)</c>, and the return type arrives from
        ///         <c>ToDisplayString</c> — which is ALREADY escaped, correctly, by the compiler's own format. So
        ///         the short name of <c>global::Demo.@event</c> is <c>@event</c>, and concatenating produces
        ///         <c>To@event</c>: an <c>@</c> in the MIDDLE of an identifier is not an escape, it is a parse
        ///         error. The same applies to the cached-mapper field name, which flattens the dots of a
        ///         fully-qualified name into underscores.
        ///     </para>
        ///     <para>
        ///         A composed name can never itself need escaping — <c>To…</c> and <c>__…</c> are not keywords
        ///         and no keyword contains one — so stripping is the whole fix and adding is always wrong here.
        ///     </para>
        /// </remarks>
        public static string Unescaped(string name)
        {
            return name.Length > 0 && name[0] == '@' ? name.Substring(1) : name;
        }

        /// <summary>
        ///     The same, for a dotted member PATH — every segment independently.
        /// </summary>
        /// <remarks>
        ///     Flattening and deep source paths carry <c>a.b.c</c> in a single name, so a blanket <c>"@" + name</c>
        ///     would produce <c>@a.b.c</c> and escape nothing that needed it. Splitting is the whole difference
        ///     between a fix and a fix that looks right.
        /// </remarks>
        public static string EscapePath(string path)
        {
            if (path.Length == 0 || path.IndexOf('.') < 0)
            {
                return Escape(path);
            }

            var result = new StringBuilder(path.Length + 4);
            var start = 0;

            while (true)
            {
                var dot = path.IndexOf('.', start);
                var segment = dot < 0 ? path.Substring(start) : path.Substring(start, dot - start);

                result.Append(Escape(segment));
                if (dot < 0)
                {
                    break;
                }

                result.Append('.');
                start = dot + 1;
            }

            return result.ToString();
        }
    }
}
