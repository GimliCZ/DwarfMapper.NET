// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     Phases of <c>ExtractCore</c>, extracted one per commit under the byte-identity lock (round 27,
    ///     R27-03).
    ///     <para>
    ///         <c>ExtractCore</c> reached 3,574 lines and cyclomatic complexity 408 — it overflows any review
    ///         window, and every new option lands inside it. The seam comments it already carried are the cut
    ///         lines: a phase becomes a method and its seam comment becomes that method's documentation, so the
    ///         structure it always had in prose becomes structure the compiler can see.
    ///     </para>
    ///     <para>
    ///         Each extraction is proven byte-identical against the 973-case golden manifest, and the corpus was
    ///         first proven to REACH every phase (<c>scripts/seam-reach.ps1</c>, 26/26) — otherwise "the manifest
    ///         is unchanged" would only mean "nothing I tested noticed".
    ///     </para>
    /// </summary>
    internal static partial class MapperExtractorPhases
    {
        // ── OnCycle = SetNull post-processing (None mode) ────────────────────────
        // After recursion-capability is finalised, flag every recursion-capable method so the
        // emitter wraps its body in the on-stack guard (TryEnterNode/ExitNode) and the public
        // entry allocates DwarfRefContext(maxDepth, setNull: true). Only reference-type pairs
        // can form a reference cycle, so value-type sources are left untouched (they keep the
        // plain depth-guarded None body — a struct cannot be its own ancestor on the stack).
        // This is the None-mode analogue of the Preserve post-pass above, but far simpler:
        // construction is unchanged (no register-before-populate, no DWARF030, no dispatch
        // wrapper) — the guard only nulls a re-entrant back-edge.
        internal static void ApplySetNullPostPass(List<Model.MapMethodModel> methods, bool isSetNullMode)
        {
            if (!isSetNullMode)
            {
                return;
            }

            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (!m.IsRecursionCapable)
                {
                    continue; // only pairs that can re-enter
                }

                // Value types never form ref cycles — but the span / async-stream models record their
                // PARAMETER (a span struct / IAsyncEnumerable) here, not their ELEMENT, and it is the
                // element pair whose converter carries the on-stack guard. Without this exemption the
                // element-wise emitters allocated their shared DwarfRefContext without `setNull: true`,
                // so the guard the converter runs had no stack set behind it.
                if (!m.ParameterIsReferenceType && !m.IsSpanMap && !m.IsAsyncStreamMap)
                {
                    continue;
                }

                methods[i] = m with
                {
                    IsSetNullMode = true
                };
            }
        }
    }
}
