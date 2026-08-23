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
