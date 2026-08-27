// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     The mapper-wide policy flags, threaded as ONE value instead of eleven parameters.
    ///     <para>
    ///         WHY THIS EXISTS. ISSUE-043 and ISSUE-044 were the same bug twice — an optional parameter defaults
    ///         to the permissive value and one call site forgets it — and the fix was to make those parameters
    ///         required. That was correct and it cost ergonomics: <c>ResolveMembers</c> reached 36 parameters,
    ///         eleven of them these flags, and a signature that long is where the NEXT threading bug hides. Ten
    ///         adjacent booleans compile just as happily transposed.
    ///     </para>
    ///     <para>
    ///         A positional record struct with NO defaults keeps exactly the property that made required
    ///         parameters the right fix — every option must still be decided, and the compiler still enforces
    ///         it — while making the decision readable. Overriding one at a call site is
    ///         <c>options with { AutoNest = false }</c>, which names what it changes; the transposition hazard
    ///         dies with the parameter list.
    ///     </para>
    ///     <para>
    ///         Only MAPPER-WIDE POLICY belongs here. Per-pair data — <c>mapValues</c>, <c>valueProviders</c>,
    ///         <c>extraParams</c>, <c>stringFormats</c> and the rest — stays on the signature, because bundling
    ///         those would turn one honest parameter list into a bag whose contents vary by caller, which is
    ///         harder to reason about rather than easier.
    ///     </para>
    ///     <para>
    ///         A <c>readonly record struct</c>, passed <c>in</c>: no allocation on a path that runs once per
    ///         mapped pair, and value equality for free should a cache key ever want it.
    ///     </para>
    /// </summary>
    internal readonly record struct MapperOptions(
        bool CaseInsensitive,
        bool AutoNest,
        bool NullAsNull,
        bool IsPreserve,
        bool IsSetNull,
        bool ImplicitConversions,
        int NameConvention,
        bool SkipNullSourceMembers,
        bool AllowNonPublic,
        bool ExplicitOnly,
        bool IgnoreObsolete);
}
