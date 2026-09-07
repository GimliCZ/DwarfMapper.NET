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
        ///         golden file for no gain. As a TYPE name it is not fine: of 29 contextual keywords run
        ///         through the generator, five broke — <c>record</c>, <c>required</c>, <c>file</c>,
        ///         <c>scoped</c> and <c>partial</c>, each of them something that may legally precede
        ///         <c>struct</c>.
        ///     </para>
        ///     <para>
        ///         Escaping rather than refusing, which is the whole point: <c>@</c> is C#'s own mechanism for
        ///         using a keyword as an identifier, so there is nothing to refuse and no list to keep. It also
        ///         dissolves the one case no syntactic check could catch — an unescaped <c>scoped</c> parses
        ///         into the same tree as a good name and fails later in the binder, while <c>@scoped</c> cannot
        ///         be read as a modifier at all, so the ambiguity has nothing to bind. The <c>@</c> is syntax:
        ///         the type's name is still <c>scoped</c>, and a consumer may write it either way.
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
