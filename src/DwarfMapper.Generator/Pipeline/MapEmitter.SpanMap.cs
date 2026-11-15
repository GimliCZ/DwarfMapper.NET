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
            var src = method.EmitParameterName;
            var dst = method.EmitSpanTargetParameterName;

            sb.Append(indent).Append(method.Accessibility).Append(" partial void ").Append(method.EmitMethodName)
                .Append('(').Append(method.ParameterTypeFullName).Append(' ').Append(src).Append(", ")
                .Append(method.ReturnTypeFullName).Append(' ').Append(dst).AppendLine(")");
            sb.Append(indent).AppendLine("{");
            AppendStackCheck(sb, method, indent);

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

            if (method.SpanMapWidens)
            {
                EmitSpanMapWiden(sb, method, indent);
                return;
            }

            // Same shared-context rule as the async-stream emission above (see EmitElementContext): a
            // ctx-carrying element converter gets ONE DwarfRefContext for the whole call, so two span slots
            // holding the same source object land the SAME target instance under Preserve. Without it the call
            // below is missing the converter's required (ctx, depth) tail: CS7036 in the generated file (B33).
            var elem = method.ElementMember;
            if (elem.ConverterMethod is not null && elem.ConverterNeedsDepthCtx)
            {
                EmitElementContext(sb, method, indent);
            }

            sb.Append(indent).Append("    for (int __i = 0; __i < ").Append(src).AppendLine(".Length; __i++)");

            // Round 29 T0.2b: the per-element expression is the SAME rule CollectionConverter.ElementExpr
            // applies to an array/list element — a Nullable<P>/nullable-annotated-reference element is LIFTED
            // (null → null, value → converter → re-wrapped), never handed bare to a synthesized helper's
            // non-nullable parameter (that was CS1503 in the consumer's build; nobody had ever declared
            // Span<P?> in the corpus before). ", __dwarf_ctx, 0" replaces the shared helper's own "ctx, depth +
            // 1" tail: the context local this method's own body declares (see EmitElementContext above) is
            // named __dwarf_ctx, and a span element is always a fresh depth-0 call, exactly like the
            // async-stream loop's element converter call just above in this file.
            var elemNh = elem.NullHandling;

            // NullableProject/NullableProjectRef reference the element TWICE (.HasValue then .Value, or
            // "is null" then the plain value) — indexing the span twice for that loses nullable flow tracking
            // between the two reads (CS8629 on the second .Value, even though it is the same slot). The question
            // is asked through CollectionConverter.ElementExprReadsItemTwice, which is a property of the
            // expression builder, so this emitter and CollectionConverter.EmitArray's bounds-check-elision fast
            // path — which substitutes "src[__i]" into that same shared expression and had this EXACT defect for
            // P?[] → Q?[] until round 29 T0.2d — apply ONE rule rather than two copies of it. Every other
            // NullHandling references the element once, so the pre-existing direct-index form is kept
            // byte-identical (it is pinned by SpanMapBlitTests' literal `src[__i]` assertions).
            var needsLocal = CollectionConverter.ElementExprReadsItemTwice(elemNh);
            if (needsLocal)
            {
                sb.Append(indent).AppendLine("    {");
                sb.Append(indent).Append("        var __item = ").Append(src).AppendLine("[__i];");
            }

            sb.Append(indent).Append("        ").Append(dst).Append("[__i] = ")
                .Append(CollectionConverter.ElementExpr(
                    needsLocal ? "__item" : src + "[__i]",
                    elem.EmitConverterMethod,
                    elemNh,
                    method.SpanTargetElementFullName,
                    elem.ConverterNeedsDepthCtx,
                    elem.SourceIsNullableRef,
                    ", __dwarf_ctx, 0",
                    // Round 29 T0.2b review fix round 1: __i is always in scope in this inline loop (unlike
                    // several CollectionConverter target shapes, which is why this argument is opt-in), so a
                    // ThrowIfNull element here can always name which index was null.
                    "__i",
                    // Round 29 T2.9: the user-declared-converter half of the forgiveness decision, resolved at
                    // the span endpoint's own resolution site (MapperExtractor.Phases) and carried here on the
                    // element MemberMap, exactly as SourceIsNullableRef is.
                    elem.ConverterParamIsNonNullableRef,
                    elem.ConverterReturnIsNullableRef))
                .AppendLine(";");

            if (needsLocal)
            {
                sb.Append(indent).AppendLine("    }");
            }

            sb.Append(indent).AppendLine("}");
        }

        /// <summary>
        ///     The body of a span map whose element pair is a lossless primitive widening (round 35, T6): the
        ///     <c>Vector.Widen</c> loop <see cref="CollectionConverter.SynthesizeSimdWidenListShape" /> emits for the
        ///     List and array shapes, written straight over the two spans the caller passed - no helper, nothing
        ///     allocated. Measured before it was written, span to span (Windows, AVX2): int to long 1.4x at 1,024
        ///     elements, float to double 4x, byte to ushort 5.8x; at 16 elements a byte pair never fills a vector and
        ///     costs about a nanosecond more than the plain loop.
        ///     <para>
        ///         Every slice stays inside both spans: the loop runs while <c>__i + __w</c> is at most the source's
        ///         length, the length guard above made the destination at least that long, and <c>__w</c> is twice
        ///         <c>__half</c> for every widen pair. Nothing past the source's length is written. The scalar tail
        ///         is the implicit widening, and it is the whole path where vectors are not hardware-accelerated -
        ///         the configuration scripts/isa-matrix.ps1 runs the runtime tests under.
        ///     </para>
        /// </summary>
        private static void EmitSpanMapWiden(StringBuilder sb, MapMethodModel method, string indent)
        {
            var src = method.EmitParameterName;
            var dst = method.EmitSpanTargetParameterName;
            var srcElem = method.SpanSourceElementFullName;
            var dstElem = method.SpanTargetElementFullName;

            sb.Append(indent).AppendLine("    int __i = 0;");
            sb.Append(indent).AppendLine("    if (global::System.Numerics.Vector.IsHardwareAccelerated)");
            sb.Append(indent).AppendLine("    {");
            sb.Append(indent).Append("        int __w = global::System.Numerics.Vector<").Append(srcElem).AppendLine(">.Count;");
            sb.Append(indent).Append("        int __half = global::System.Numerics.Vector<").Append(dstElem).AppendLine(">.Count;");
            sb.Append(indent).Append("        for (; __i + __w <= ").Append(src).AppendLine(".Length; __i += __w)");
            sb.Append(indent).AppendLine("        {");
            sb.Append(indent).Append("            var __v = new global::System.Numerics.Vector<").Append(srcElem).Append(">(")
                .Append(src).AppendLine(".Slice(__i));");
            sb.Append(indent).AppendLine("            global::System.Numerics.Vector.Widen(__v, out var __lo, out var __hi);");
            sb.Append(indent).Append("            __lo.CopyTo(").Append(dst).AppendLine(".Slice(__i));");
            sb.Append(indent).Append("            __hi.CopyTo(").Append(dst).AppendLine(".Slice(__i + __half));");
            sb.Append(indent).AppendLine("        }");
            sb.Append(indent).AppendLine("    }");
            sb.Append(indent).Append("    for (; __i < ").Append(src).Append(".Length; __i++) ")
                .Append(dst).Append("[__i] = ").Append(src).AppendLine("[__i];");
            sb.Append(indent).AppendLine("}");
        }
    }
}
