// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     Turns <see cref="DwarfMapperAttribute.SkipNullSourceMembers" /> on (or off) for <b>one mapping method</b>,
///     overriding the mapper- and assembly-level setting.
/// </summary>
/// <remarks>
///     <para>
///         With it, a <see langword="null" /> source member never overwrites the destination's current value:
///         the generated code emits <c>if (src.X is not null) dst.X = …;</c> for nullable-source,
///         post-construction-settable members. That is the patch-merge idiom — the thing AutoMapper spelled
///         <c>ForAllMembers(o =&gt; o.Condition((_, _, src) =&gt; src != null))</c>.
///     </para>
///     <para>
///         <b>Why this exists.</b> AutoMapper's <c>ForAllMembers</c> was configured <i>per map</i>, while
///         <c>[DwarfMapper(SkipNullSourceMembers = true)]</c> is a whole-class policy. A profile that mixed
///         patch-merge maps with ordinary ones therefore could not be translated one-for-one — it had to be
///         split across two mapper classes purely to carry one boolean. Worse, that split had a real
///         behavioural consequence: a nested pair reached from both classes was synthesized twice, once
///         guarded and once not, silently.
///     </para>
///     <para>
///         Prefer the class-level option when it fits — one statement of intent is easier to read than many.
///         Reach for this when a mapper genuinely has both kinds of map, and use
///         <c>[MapNullSkip(false)]</c> to carve one method out of a class that enables it.
///     </para>
///     <para>
///         <b>Precedence.</b> The setting is resolved most-specific-wins: this attribute, then the pair-scoped
///         <see cref="MapNullSkipAttribute{TSource,TTarget}" />, then
///         <see cref="DwarfMapperAttribute.SkipNullSourceMembers" /> on the mapper, then the assembly default.
///         So a method carrying <c>[MapNullSkip(false)]</c> replaces rather than patches even when a
///         <c>[MapNullSkip&lt;TSource, TTarget&gt;]</c> on the same class names that method's pair — carving a
///         method out is the whole reason this form exists, which only works if it outranks what it is carving
///         out of.
///     </para>
///     <para>
///         <b>It does not reach the element-wise endpoints.</b> A span map or an async-stream map resolves no
///         members of its own: it maps each element through a mapper synthesized per <c>(source, target)</c>
///         and shared by every route to that pair, so only a directive that NAMES the pair can configure it.
///         Written on such a method this is reported as <c>DWARF090</c>, which names
///         <c>[MapNullSkip&lt;TSource, TTarget&gt;]</c> as the form that does apply there — a refusal rather
///         than the silence it used to be.
///     </para>
///     <example>
///         <code>
/// [DwarfMapper]
/// public partial class SettingsMappers
/// {
///     // Full replace: a null in the DTO means "clear this".
///     public partial void Replace(SettingsDto src, Settings dst);
///
///     // Patch-merge: a null in the DTO means "leave this alone".
///     [MapNullSkip]
///     public partial void Patch(SettingsDto src, Settings dst);
/// }
/// </code>
///     </example>
///     <para>
///         Non-nullable value-type sources and <c>required</c>/<c>init</c>-only targets are unaffected — there
///         is no null to skip, and no post-construction assignment to guard.
///     </para>
/// </remarks>
[DwarfSurface(SurfaceCategory.ConsumerDirective, ProbeKey = "nullable-source-nonnull-target")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MapNullSkipAttribute : Attribute
{
    /// <summary>Enables null-skipping for this method (the default). Pass <c>false</c> to disable it.</summary>
    /// <param name="enabled">
    ///     <see langword="true" /> to skip null source members for this method, <see langword="false" /> to
    ///     force the ordinary replace behaviour even when the mapper or assembly enables skipping.
    /// </param>
    public MapNullSkipAttribute(bool enabled = true)
    {
        Enabled = enabled;
    }

    /// <summary>Whether null source members are skipped for this method.</summary>
    public bool Enabled { get; }
}

/// <summary>
///     Turns <see cref="DwarfMapperAttribute.SkipNullSourceMembers" /> on (or off) for <b>one declared
///     <c>[GenerateMap&lt;TSource, TTarget&gt;]</c> pair</b>, overriding the mapper- and assembly-level setting.
/// </summary>
/// <remarks>
///     The pair-scoped member of the <c>[MapProperty&lt;S,T&gt;]</c> / <c>[MapIgnore&lt;T&gt;]</c> /
///     <c>[MapValue&lt;T&gt;]</c> family, for mappers that declare their pairs as attributes rather than as
///     partial methods. See <see cref="MapNullSkipAttribute" /> for what the option does and why it needs a
///     scope narrower than the class.
///     <para>
///         This form is the one that reaches EVERY mapping shape. A pair named here configures the pair
///         wherever it is mapped from — a <c>[GenerateMap]</c> pair, a partial <c>Map</c>/<c>Update</c> method
///         over the same two types, the element pair of a span or async-stream map, and a nested member pair
///         reached from any of them — because the mapper synthesized for a pair is shared by every route to it
///         and a directive that names the pair is the only kind it can take configuration from. Where the
///         method-scoped <see cref="MapNullSkipAttribute" /> cannot reach, <c>DWARF090</c> names this attribute
///         as the replacement.
///     </para>
///     <para>
///         It is outranked by <see cref="MapNullSkipAttribute" /> on a method, and outranks
///         <see cref="DwarfMapperAttribute.SkipNullSourceMembers" /> — see that type for the full precedence
///         chain. Declaring the same pair twice with opposite values is legal (this attribute is
///         <c>AllowMultiple</c>) and the first declaration wins; do not rely on it.
///     </para>
///     <example>
///         <code>
/// [DwarfMapper]
/// [GenerateMap&lt;SettingsDto, Settings&gt;]                 // full replace
/// [GenerateMap&lt;PatchDto, Settings&gt;]                     // patch-merge:
/// [MapNullSkip&lt;PatchDto, Settings&gt;]                     //   nulls leave the destination alone
/// public partial class SettingsMappers { }
/// </code>
///     </example>
/// </remarks>
/// <typeparam name="TSource">The source type of the pair this applies to.</typeparam>
/// <typeparam name="TTarget">The destination type of the pair this applies to.</typeparam>
[DwarfSurface(SurfaceCategory.ConsumerDirective, ProbeKey = "nullable-source-nonnull-target")]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class MapNullSkipAttribute<TSource, TTarget> : Attribute
{
    /// <inheritdoc cref="MapNullSkipAttribute(bool)" />
    public MapNullSkipAttribute(bool enabled = true)
    {
        Enabled = enabled;
    }

    /// <summary>Whether null source members are skipped for this pair.</summary>
    public bool Enabled { get; }
}
