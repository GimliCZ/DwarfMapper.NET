// SPDX-License-Identifier: GPL-2.0-only

using System.Diagnostics.CodeAnalysis;

namespace DwarfMapper
{
    /// <summary>
    ///     Per-method override for the <see cref="DwarfMapperAttribute.AutoNest" /> class-level setting.
    ///     Applying <c>[AutoNest(false)]</c> to a single mapping method disables auto-synthesis of nested
    ///     object mappers for that method, even when the enclosing class has <c>AutoNest = true</c>.
    /// </summary>
    [DwarfSurface(SurfaceCategory.ConsumerDirective, ProbeKey = "nested-pair")]
// Only `false` asks anything. Auto-nesting is already on by default, so [AutoNest(true)] restates the ambient
// state and cannot change a byte — the matrix's sampled `true` produced a cell that read "silent" for a
// reason that had nothing to do with the generator. Which bool bites is not derivable: [MapNullSkip(true)] is
// the biting value of the same-shaped constructor next door, so it is stated here rather than guessed.
    [DwarfSurfaceProbe(1, Arguments = "false")]
    [ExcludeFromCodeCoverage(Justification = "compile-time-only attribute, consumed by the generator (round-22 P4's one " + "sanctioned category): read from the semantic model at build time; no runtime code path constructs or " + "executes it. Issues/round22/RESEARCH-97-PERCENT-GATES.md §2.2.")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class AutoNestAttribute : Attribute
    {
        /// <summary>
        ///     Initialises the attribute with the given auto-nest value.
        /// </summary>
        /// <param name="enabled">
        ///     <c>true</c> to enable auto-nesting for this method; <c>false</c> to disable it.
        /// </param>
        public AutoNestAttribute(bool enabled = true)
        {
            Enabled = enabled;
        }

        /// <summary>Gets whether auto-nesting is enabled for this method.</summary>
        public bool Enabled { get; }
    }
}
