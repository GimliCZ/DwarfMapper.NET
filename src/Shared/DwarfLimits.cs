// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper
{
    /// <summary>
    ///     The recursion bounds, declared ONCE and compiled into both the runtime and the generator.
    ///     <para>
    ///         These are a resource limit — the guard against unbounded recursion on a cyclic object graph
    ///         exhausting the stack — so they are security-relevant rather than merely a tuning knob.
    ///     </para>
    ///     <para>
    ///         WHY THIS FILE EXISTS. The bound was enforced in two places that could not see each other.
    ///         <c>DwarfRefContext</c> clamped to its own <c>AbsoluteMaxDepth</c>, while the generator clamped to a
    ///         hard-coded literal <c>1000</c> carrying the comment "matches DwarfRefContext.AbsoluteMaxDepth" — a
    ///         claim nothing verified. The generator targets <c>netstandard2.0</c> and does not reference the
    ///         runtime, so it genuinely could not name the constant; that constraint is real and is not the
    ///         defect. The defect was that a security-relevant bound lived in two independently maintained
    ///         places with no test on either side, which is the "fix applied to 1 of N identical sites" shape
    ///         this repository has been bitten by before.
    ///     </para>
    ///     <para>
    ///         Raising <c>AbsoluteMaxDepth</c> without the generator following would have silently capped users
    ///         below the documented bound: the runtime would permit the deeper value and the generator would go
    ///         on refusing it. Linking one file into both projects removes the possibility rather than testing
    ///         for it — the stronger form, and the reason this is a shared source file rather than a scan.
    ///     </para>
    ///     <para>
    ///         Linked, not shared by project reference, because a project reference is exactly what the
    ///         <c>netstandard2.0</c> Roslyn-host requirement forbids. Same
    ///         <c>&lt;Compile Include="..\Shared\…" Link="…"/&gt;</c> idiom the test projects already use for
    ///         <c>DeepTier.cs</c>.
    ///     </para>
    /// </summary>
    internal static class DwarfLimits
    {
        /// <summary>
        ///     The absolute hard cap on mapping recursion depth. No <c>[DwarfMapper(MaxDepth = N)]</c> value can
        ///     exceed it, in either the generator's validation or the runtime's clamp.
        /// </summary>
        internal const int AbsoluteMaxDepth = 1000;

        /// <summary>
        ///     The floor. A mapper that refuses at depth zero would throw before doing any work, so a configured
        ///     value below this is clamped up rather than honoured.
        /// </summary>
        internal const int MinMaxDepth = 1;

        /// <summary>The depth used when <c>[DwarfMapper(MaxDepth = …)]</c> is not specified.</summary>
        internal const int DefaultMaxDepth = 64;
    }
}
