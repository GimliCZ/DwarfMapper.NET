// SPDX-License-Identifier: GPL-2.0-only

using System.Text;
using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     Honors <b>user-defined conversion operators</b> (a type's <c>implicit</c>/<c>explicit operator</c>)
    ///     between a source and destination member type — e.g. a strong-type <c>UserId</c> that declares
    ///     <c>implicit operator int</c> mapped to an <c>int</c> DTO field.
    ///     <para>
    ///         The built-in conversion classifier used elsewhere deliberately excludes user-defined conversions
    ///         (see <c>HasImplicitConversion</c>), so this converter is the dedicated path for them. It is wired LAST
    ///         in <c>TryResolveConversion</c> — just before <c>DWARF005</c> — so identity, built-in implicit, numeric,
    ///         parse/format, enum, and auto-nesting all keep precedence; this only rescues pairs that would otherwise
    ///         fail. A direct cast <c>(TTarget)v</c> invokes the operator (implicit or explicit) and is a concrete
    ///         static call — reflection-free and AOT/trim-safe.
    ///     </para>
    /// </summary>
    internal static class UserConversionConverter
    {
        /// <summary>
        ///     Returns a synthesized converter-method name when a user-defined conversion operator exists from
        ///     <paramref name="src" /> to <paramref name="tgt" />; <c>null</c> otherwise. <paramref name="isExplicit" />
        ///     is <c>true</c> when the operator is <c>explicit</c> (a potentially-lossy conversion the caller should
        ///     surface as <c>DWARF038</c>).
        /// </summary>
        public static string? TryCreate(
            Compilation compilation,
            ITypeSymbol src,
            ITypeSymbol tgt,
            Dictionary<string, SynthesizedMethod> synthesized,
            out bool isExplicit)
        {
            isExplicit = false;

            if (!Exists(compilation, src, tgt, out var conv))
            {
                return null;
            }

            isExplicit = conv.IsExplicit;

            var name = MethodName(src, tgt);
            if (!synthesized.ContainsKey(name))
            {
                var fqSrc = Fq(src);
                var fqTgt = Fq(tgt);
                // An explicit cast invokes a user-defined implicit OR explicit operator.
                var code = $"    private static {fqTgt} {name}({fqSrc} v) => ({fqTgt})v;\n";
                synthesized[name] = new SynthesizedMethod(name, code);
            }

            return name;
        }

        /// <summary>
        ///     True when a user-defined conversion operator exists from <paramref name="src" /> to
        ///     <paramref name="tgt" /> — the QUESTION <see cref="TryCreate" /> answers by SYNTHESIZING, asked
        ///     without writing anything.
        /// </summary>
        /// <remarks>
        ///     Round 29 T0.2c: the array/list blit gate must know whether the element pair's resolution would
        ///     land on a user's own operator, and it asks BEFORE resolution runs — so it cannot afford
        ///     <see cref="TryCreate" />'s synthesis side effect (a helper emitted for a pair the blit may then
        ///     take anyway). Both share this one classification so "there is an operator" cannot drift between
        ///     the asker and the adopter.
        /// </remarks>
        public static bool Exists(Compilation compilation, ITypeSymbol src, ITypeSymbol tgt)
        {
            return Exists(compilation, src, tgt, out _);
        }

        private static bool Exists(Compilation compilation, ITypeSymbol src, ITypeSymbol tgt, out Conversion conversion)
        {
            conversion = ((CSharpCompilation)compilation).ClassifyConversion(src, tgt);
            return conversion.Exists && conversion.IsUserDefined;
        }

        private static string Fq(ITypeSymbol t)
        {
            return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private static string MethodName(ITypeSymbol src, ITypeSymbol tgt)
        {
            return GeneratedNames.UserConv + Short(src) + "_To_" + Short(tgt) + "_" + StableHash.Fnv1a(Fq(src) + "|" + Fq(tgt));
        }

        private static string Short(ITypeSymbol t)
        {
            var sb = new StringBuilder(t.Name.Length);
            foreach (var ch in t.Name) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            return sb.Length == 0 ? "_" : sb.ToString();
        }
    }
}
