// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;

namespace DwarfMapper.Generator.Model
{
    /// <summary>A discovered round-trip pair to emit a verifier for.</summary>
    public sealed record RoundTripPair(
        string ForwardName,
        string BackwardName,
        string SourceTypeFullName,
        string DtoTypeFullName) : IEquatable<RoundTripPair>
    {
        /// <summary><see cref="ForwardName" /> as it must be written into emitted C#.</summary>
        /// <remarks>
        ///     Both halves of a round-trip pair are methods the consumer declared and named. The generated
        ///     verifier both NAMES the pair in its own method name and PASSES both as method groups, and only
        ///     the latter needs escaping — see <see cref="EmitVerifierSuffix" />.
        /// </remarks>
        public string EmitForwardName => Identifiers.Escape(ForwardName);

        /// <summary><see cref="BackwardName" /> as it must be written into emitted C#.</summary>
        public string EmitBackwardName => Identifiers.Escape(BackwardName);

        /// <summary>
        ///     The forward name as it appears INSIDE the generated verifier's own name
        ///     (<c>VerifyRoundTrip_&lt;suffix&gt;</c>) — never escaped, because an <c>@</c> in the middle of an
        ///     identifier is not an escape, it is a parse error.
        /// </summary>
        /// <remarks>
        ///     The distinction is the one family-G failure this round measured twice: a COMPOSED identifier
        ///     takes the raw name and cannot itself be a keyword, while a name emitted whole takes the escape.
        ///     Spelling it as a named member rather than leaving <c>ForwardName</c> bare at the call site is
        ///     what lets the emission scan ban the raw field outright.
        /// </remarks>
        public string EmitVerifierSuffix => ForwardName;
    }
}
