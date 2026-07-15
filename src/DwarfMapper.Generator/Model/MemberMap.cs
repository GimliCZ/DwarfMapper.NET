// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Model;

/// <summary>
///     One resolved destination&lt;-source member assignment. <see cref="ConverterMethod" />,
///     when set, transforms the value; otherwise <see cref="NullHandling" /> controls
///     how a nullable value-type source is unwrapped.
/// </summary>
public sealed record MemberMap(
    string TargetName,
    string SourceName,
    string? ConverterMethod = null,
    NullHandling NullHandling = NullHandling.None,
    /// <summary>
    /// When <c>true</c>, <see cref="ConverterMethod"/> is a recursion-capable synthesized method
    /// that requires <c>(ctx, depth + 1)</c> extra arguments at call sites.
    /// Set by <see cref="DwarfMapper.Generator.Pipeline.MapperExtractor"/> after the
    /// recursion-capability analysis (Plan 19 C1).
    /// </summary>
    bool ConverterNeedsDepthCtx = false,
    /// <summary>
    /// When <c>true</c>, the source member expression is a nullable reference type and the
    /// emitter must add a <c>!</c> null-forgiving operator when passing it to the converter.
    /// Only set when <see cref="ConverterMethod"/> is a synthesized nested mapper that starts
    /// with a null-guard (<c>if (s is null) return null!</c>), so null is handled safely.
    /// </summary>
    bool SourceIsNullableRef = false,
    /// <summary>
    /// When non-null, this raw C# expression is emitted verbatim as the member's value, bypassing all
    /// source-member access / converter / null-handling logic. Used by <c>[MapValue]</c> for constant
    /// literals (e.g. <c>"api-v2"</c>) and computed providers (e.g. <c>Now()</c>). <see cref="SourceName"/>
    /// is empty in this case.
    /// </summary>
    string? ValueExpression = null,
    /// <summary>
    /// When non-null, this member is an <b>unflatten</b> assignment whose <see cref="TargetName"/> is a
    /// single-level dotted path (e.g. <c>"Address.City"</c>); this is the fully-qualified type of the
    /// intermediate root member (<c>Address</c>), which the emitter instantiates if null
    /// (<c>if (t.Address is null) t.Address = new …();</c>) before assigning the leaf. Single intermediate
    /// only.
    /// </summary>
    string? UnflattenIntermediateFqn = null,
    /// <summary>
    /// When non-null, a constant literal substituted when the (nullable) source member is null:
    /// emitted as <c>param.Source ?? &lt;literal&gt;</c>. Set by <c>[MapProperty(NullSubstitute = …)]</c>
    /// (direct-assignable members only).
    /// </summary>
    string? NullSubstituteLiteral = null,
    /// <summary>
    /// When non-null, a parameterless-on-source predicate guarding this assignment: the emitter writes
    /// <c>if (Predicate(param)) target.Member = …;</c> post-construction. Set by
    /// <c>[MapProperty(When = nameof(...))]</c>; the member keeps its destination default when false.
    /// </summary>
    string? WhenPredicate = null,
    /// <summary>
    /// When <c>true</c>, this assignment is guarded by an inline source-not-null check emitted
    /// post-construction: <c>if (param.Source is not null) target.Member = …;</c> — so a null source member
    /// keeps the destination's default rather than overwriting it. Set by
    /// <c>[DwarfMapper(SkipNullSourceMembers = true)]</c> for nullable-source, post-construction-settable
    /// members (AutoMapper's <c>ForAllMembers(o =&gt; o.Condition((_,_,src) =&gt; src != null))</c>).
    /// Mutually exclusive with <see cref="WhenPredicate"/>.
    /// </summary>
    bool SkipIfSourceNull = false,
    /// <summary>
    /// When <c>true</c>, a nullable-annotated REFERENCE source is being raw-assigned to a non-nullable
    /// reference target (the documented <see cref="DwarfMapper.NullStrategy"/> contract: it governs nullable
    /// VALUE types only). The assignment is intentional, but it makes the C# compiler emit CS8601 from inside
    /// the generated file — an unfixable warning for a consumer with TreatWarningsAsErrors, in code they cannot
    /// edit. The emitter therefore appends the null-forgiving <c>!</c> to silence CS8601, and DwarfMapper
    /// reports DWARF070 against the user's own DTO instead: same signal, but actionable and suppressible.
    /// Set only for the direct-assign path (no converter, no null-handling, no NullSubstitute) and cleared
    /// when <see cref="SkipIfSourceNull"/> already guards the null.
    /// </summary>
    bool NullRefIntoNonNullable = false,
    /// <summary>
    /// When non-null, this is an <b>update-into key-based upsert</b> of a <c>List&lt;T&gt;</c> member (set by
    /// <c>[MapCollectionKey]</c>): the emitter merges the source list into the existing one by this key member
    /// rather than replacing it — matched keys update the slot, new keys are added, unmatched existing elements
    /// are kept. <see cref="UpsertKeyMember"/> is the element-type key member; <see cref="UpsertKeyTypeFqn"/>
    /// its type (for the index dictionary). v1: element type identical on both sides.
    /// </summary>
    string? UpsertKeyMember = null,
    string? UpsertKeyTypeFqn = null) : IEquatable<MemberMap>;
