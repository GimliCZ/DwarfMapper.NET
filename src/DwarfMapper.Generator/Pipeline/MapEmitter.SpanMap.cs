// SPDX-License-Identifier: GPL-2.0-only

using System.Text;
using DwarfMapper.Generator.Model;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapEmitter
    {
        /// <summary>
        ///     Emits a zero-alloc span map <c>void Map(ReadOnlySpan&lt;S&gt; src, Span&lt;D&gt; dst)</c>: a defensive
        ///     length guard (destination too small → <c>ArgumentException</c>, never silent truncation) then either
        ///     a single block copy (<see cref="MapMethodModel.SpanMapBlits" /> — the element pair is proven
        ///     layout-identical, see <see cref="BlittableProof.CanReinterpret" />) or an element loop
        ///     <c>dst[i] = conv(src[i])</c> (or a direct/implicit assignment when no converter is needed). No
        ///     allocation either way; the caller owns the destination buffer.
        /// </summary>
        private static void EmitSpanMapMethod(StringBuilder sb, MapMethodModel method, string indent)
        {
            var src = method.ParameterName;
            var dst = method.SpanTargetParameterName;

            sb.Append(indent).Append(method.Accessibility).Append(" partial void ").Append(method.MethodName)
                .Append('(').Append(method.ParameterTypeFullName).Append(' ').Append(src).Append(", ")
                .Append(method.ReturnTypeFullName).Append(' ').Append(dst).AppendLine(")");
            sb.Append(indent).AppendLine("{");

            // Defensive length guard — never silently truncate.
            sb.Append(indent).Append("    if (").Append(dst).Append(".Length < ").Append(src).AppendLine(".Length)");
            sb.Append(indent).Append("        throw new global::System.ArgumentException(")
                .Append("\"DwarfMapper: destination span (length \" + ").Append(dst)
                .Append(".Length + \") is smaller than the source span (length \" + ")
                .Append(src).Append(".Length + \").\", nameof(").Append(dst).AppendLine("));");

            if (method.SpanMapBlits)
            {
                // Proven at generation time (BlittableProof): same bytes, so the caller-owned buffer is filled by
                // one block move. Zero allocation and the copy floor — Issues/round29 §9: 0.20x at 1k against the
                // element loop, neutral above the cache. No hardware gate: MemoryMarshal.Cast + CopyTo is the
                // runtime's memmove.
                sb.Append(indent).Append("    global::System.Runtime.InteropServices.MemoryMarshal.Cast<")
                    .Append(method.SpanSourceElementFullName).Append(", ").Append(method.SpanTargetElementFullName)
                    .Append(">(").Append(src).Append(").CopyTo(").Append(dst).AppendLine(");");
                sb.Append(indent).AppendLine("}");
                return;
            }

            // Same shared-context rule as the async-stream emission above (see EmitElementContext): a
            // ctx-carrying element converter gets ONE DwarfRefContext for the whole call, so two span slots
            // holding the same source object land the SAME target instance under Preserve. Without it the call
            // below is missing the converter's required (ctx, depth) tail: CS7036 in the generated file (B33).
            var elem = method.Members.Count > 0 ? method.Members[0] : null;
            if (elem?.ConverterMethod is not null && elem.ConverterNeedsDepthCtx)
            {
                EmitElementContext(sb, method, indent);
            }

            sb.Append(indent).Append("    for (int __i = 0; __i < ").Append(src).AppendLine(".Length; __i++)");
            sb.Append(indent).Append("        ").Append(dst).Append("[__i] = ");

            if (elem?.ConverterMethod is null)
                // Direct/implicit element assignment (e.g. int → long widening).
            {
                sb.Append(src).Append("[__i]");
            }
            else
            {
                sb.Append(elem.ConverterMethod).Append('(').Append(src).Append("[__i]");
                if (elem.ConverterNeedsDepthCtx)
                {
                    sb.Append(", __dwarf_ctx, 0");
                }

                sb.Append(')');
            }

            sb.AppendLine(";");

            sb.Append(indent).AppendLine("}");
        }
    }
}
