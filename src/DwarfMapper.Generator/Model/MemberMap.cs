// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;

namespace DwarfMapper.Generator.Model
{
    /// <summary>
    ///     One resolved destination&lt;-source member assignment. <see cref="ConverterMethod" />,
    ///     when set, transforms the value; otherwise <see cref="NullHandling" /> controls
    ///     how a nullable value-type source is unwrapped.
    /// </summary>
    /// <param name="ConverterNeedsDepthCtx">
    ///     When <c>true</c>, <see cref="ConverterMethod"/> is a recursion-capable synthesized method
    ///     that requires <c>(ctx, depth + 1)</c> extra arguments at call sites.
    ///     Set by <see cref="DwarfMapper.Generator.Pipeline.MapperExtractor"/> after the
    ///     recursion-capability analysis (Plan 19 C1).
    /// </param>
    /// <param name="SourceIsNullableRef">
    ///     When <c>true</c>, the source member expression is a nullable reference type and the
    ///     emitter must add a <c>!</c> null-forgiving operator when passing it to the converter.
    ///     Only set when <see cref="ConverterMethod"/> is a synthesized nested mapper that starts
    ///     with a null-guard (<c>if (s is null) return null!</c>), so null is handled safely.
    /// </param>
    /// <param name="ValueExpression">
    ///     When non-null, this raw C# expression is emitted verbatim as the member's value, bypassing all
    ///     source-member access / converter / null-handling logic. Used by <c>[MapValue]</c> for constant
    ///     literals (e.g. <c>"api-v2"</c>) and computed providers (e.g. <c>Now()</c>). <see cref="SourceName"/>
    ///     is empty in this case.
    /// </param>
    /// <param name="UnflattenIntermediateFqn">
    ///     When non-null, this member is an <b>unflatten</b> assignment whose <see cref="TargetName"/> is a
    ///     single-level dotted path (e.g. <c>"Address.City"</c>); this is the fully-qualified type of the
    ///     intermediate root member (<c>Address</c>), which the emitter instantiates if null
    ///     (<c>if (t.Address is null) t.Address = new …();</c>) before assigning the leaf. Single intermediate
    ///     only.
    /// </param>
    /// <param name="NullSubstituteLiteral">
    ///     When non-null, a constant literal substituted when the (nullable) source member is null:
    ///     emitted as <c>param.Source ?? &lt;literal&gt;</c>. Set by <c>[MapProperty(NullSubstitute = …)]</c>
    ///     (direct-assignable members only).
    /// </param>
    /// <param name="WhenPredicate">
    ///     When non-null, a parameterless-on-source predicate guarding this assignment: the emitter writes
    ///     <c>if (Predicate(param)) target.Member = …;</c> post-construction. Set by
    ///     <c>[MapProperty(When = nameof(...))]</c>; the member keeps its destination default when false.
    /// </param>
    /// <param name="SkipIfSourceNull">
    ///     When <c>true</c>, this assignment is guarded by an inline source-not-null check emitted
    ///     post-construction: <c>if (param.Source is not null) target.Member = …;</c> — so a null source member
    ///     keeps the destination's default rather than overwriting it. Set by
    ///     <c>[DwarfMapper(SkipNullSourceMembers = true)]</c> for nullable-source, post-construction-settable
    ///     members (AutoMapper's <c>ForAllMembers(o =&gt; o.Condition((_,_,src) =&gt; src != null))</c>).
    ///     Mutually exclusive with <see cref="WhenPredicate"/>.
    /// </param>
    /// <param name="NullRefIntoNonNullable">
    ///     When <c>true</c>, a nullable-annotated REFERENCE source is being raw-assigned to a non-nullable
    ///     reference target (the documented <c>DwarfMapper.NullStrategy</c> contract: it governs nullable
    ///     VALUE types only). The assignment is intentional, but it makes the C# compiler emit CS8601 from inside
    ///     the generated file — an unfixable warning for a consumer with TreatWarningsAsErrors, in code they cannot
    ///     edit. The emitter therefore appends the null-forgiving <c>!</c> to silence CS8601, and DwarfMapper
    ///     reports DWARF070 against the user's own DTO instead: same signal, but actionable and suppressible.
    ///     Set only for the direct-assign path (no converter, no null-handling, no NullSubstitute) and cleared
    ///     when <see cref="SkipIfSourceNull"/> already guards the null.
    /// </param>
    /// <param name="UpsertKeyMember">
    ///     When non-null, this is an <b>update-into key-based upsert</b> of a <c>List&lt;T&gt;</c> member (set by
    ///     <c>[MapCollectionKey]</c>): the emitter merges the source list into the existing one by this key member
    ///     rather than replacing it — matched keys update the slot, new keys are added, unmatched existing elements
    ///     are kept. <see cref="UpsertKeyMember"/> is the element-type key member; <see cref="UpsertKeyTypeFqn"/>
    ///     its type (for the index dictionary). v1: element type identical on both sides.
    /// </param>
    /// <param name="ConverterParamIsNonNullableRef">
    ///     When <c>true</c>, <see cref="ConverterMethod"/> is a user-declared map/converter method whose parameter
    ///     is a NON-nullable reference type, and <see cref="SourceIsNullableRef"/> is true — i.e. a possibly-null
    ///     source is being passed into a converter that cannot accept null. The emitter must null-forgive the
    ///     argument (<c>Conv(s.X!)</c>) so the call compiles (else CS8604), exactly as it already does for
    ///     synthesized nested mappers via <c>IsSynthesized</c>. A genuine null then throws loudly inside the
    ///     callee's own <c>ArgumentNullException.ThrowIfNull</c> rather than being smuggled in. Distinguished from a
    ///     null-TOLERANT user converter (one declared with a nullable parameter), which does NOT set this and so is
    ///     never forgiven — dropping a null it was written to accept. Set only for the user-declared converter path;
    ///     synthesized helpers keep flowing through <c>IsSynthesized</c>.
    /// </param>
    /// <param name="ConverterReturnIsNullableRef">
    ///     When <c>true</c>, <see cref="ConverterMethod" /> is a user-declared map/converter method whose RETURN is
    ///     a nullable-annotated reference, and the destination this member writes into is NOT — so the call's
    ///     result must be null-forgiven (<c>Conv(s.X)!</c>) or the C# compiler raises CS8600/CS8601/CS8603/CS8604
    ///     from inside the generated file. The mirror of <see cref="ConverterParamIsNonNullableRef" />, which
    ///     carried only the ARGUMENT side: <c>partial ChildDto? ToDto(Child c)</c> feeding a non-nullable
    ///     <c>ChildDto Inner</c> emitted <c>Inner = s.Inner is null ? null! : ToDto(s.Inner)</c> — the null arm
    ///     forgiven, the call not (round 29 task 2.8 concern 1, fixed by task 2.9).
    ///     <para>
    ///         Unlike the argument side, this forgiveness genuinely STORES a null in a slot whose type forbids it —
    ///         the argument side only defers to the callee's own <c>ArgumentNullException.ThrowIfNull</c>. That is
    ///         why it is never set without <c>DWARF107</c> being reported at the same moment, by the one decision
    ///         in <c>MapperExtractor.ForgiveConverterNullableReturn</c>.
    ///     </para>
    /// </param>
    /// <param name="SourceAccessExpression">
    ///     When non-null, the member's value is read from this raw C# expression instead of
    ///     <c>param.Member</c> — and, unlike <see cref="ValueExpression" />, the converter and
    ///     <see cref="NullHandling" /> still apply ON TOP of it. Set for a <b>Phase 5 extra parameter</b>, whose
    ///     value is a bare identifier in scope rather than a member of the source object.
    ///     <para>
    ///         WHY IT IS NOT <see cref="ValueExpression" />. The extra-parameter phase used to hand the emitter a
    ///         finished <c>Conv(p)</c> string, which short-circuits <c>AppendValueExpression</c> before any
    ///         null handling is consulted — so a nullable extra parameter was emitted bare: <c>Count = count</c>
    ///         for <c>int? → int</c> (CS0266, a compile ERROR in the consumer's .g.cs), <c>ToDto(inner)</c> for
    ///         <c>Child? → ChildDto</c> (CS8604) and <c>Inner = inner</c> for <c>Child? → Child</c> (CS8601).
    ///         Carrying the ACCESS rather than the finished value lets the extra parameter flow through the one
    ///         emitter switch every other edge reads, instead of re-spelling <c>null</c> / <c>null!</c> / <c>!</c>
    ///         at a fifth site.
    ///     </para>
    ///     <para>
    ///         <see cref="SourceName" /> stays empty, exactly as it was: every pass that treats
    ///         <see cref="SourceName" /> as a path into the SOURCE TYPE (the <c>SkipNullSourceMembers</c> marking,
    ///         the consumed-source-member analysis, flatten-root accounting) must keep skipping this member — a
    ///         parameter named <c>inner</c> is not the source member <c>inner</c>.
    ///     </para>
    /// </param>
    public sealed record MemberMap(
        string TargetName,
        string SourceName,
        string? ConverterMethod = null,
        NullHandling NullHandling = NullHandling.None,
        bool ConverterNeedsDepthCtx = false,
        bool SourceIsNullableRef = false,
        string? ValueExpression = null,
        string? UnflattenIntermediateFqn = null,
        string? NullSubstituteLiteral = null,
        string? WhenPredicate = null,
        bool SkipIfSourceNull = false,
        bool NullRefIntoNonNullable = false,
        string? UpsertKeyMember = null,
        string? UpsertKeyTypeFqn = null,
        bool ConverterParamIsNonNullableRef = false,
        string? SourceAccessExpression = null,
        bool ConverterReturnIsNullableRef = false) : IEquatable<MemberMap>
    {
        /// <summary>
        ///     <see cref="TargetName" /> as it must be written into emitted C# — <c>class</c> becomes <c>@class</c>.
        /// </summary>
        /// <remarks>
        ///     Kept separate from the raw name rather than replacing it. <see cref="TargetName" /> is COMPARED
        ///     (DWARF081's member list, the <c>[RestatesBase]</c> drift check, its <c>Overrides</c> matching) and
        ///     printed in diagnostics, and <c>nameof(Dto.@class)</c> yields <c>"class"</c> — so escaping in place
        ///     would silently break every one of those. Emission is the only place the <c>@</c> belongs.
        /// </remarks>
        public string EmitTargetName => Identifiers.EscapePath(TargetName);

        /// <summary><see cref="SourceName" /> as it must be written into emitted C#. See <see cref="EmitTargetName" />.</summary>
        public string EmitSourceName => Identifiers.EscapePath(SourceName);

        /// <summary>
        ///     <see cref="ConverterMethod" /> as it must be written into emitted C# — the name is emitted as a
        ///     CALL, and it may be a consumer's own method (a discovered user conversion, a
        ///     <c>[MapProperty(Use = …)]</c> target) rather than a synthesized one.
        /// </summary>
        /// <remarks>
        ///     The raw field remains the call-graph edge label: <c>MapperExtractor</c> matches it against
        ///     <see cref="MapMethodModel.MethodName" /> by ordinal equality to find self-recursion, to inject the
        ///     depth companion, and to re-synthesize helpers, so escaping it in place would make those edges miss.
        /// </remarks>
        public string? EmitConverterMethod =>
            ConverterMethod is null ? null : Identifiers.Escape(ConverterMethod);

        /// <summary>
        ///     <see cref="WhenPredicate" /> as it must be written into emitted C# — a
        ///     <c>[MapProperty(When = …)]</c> predicate, emitted as the condition of an <c>if</c>.
        /// </summary>
        public string? EmitWhenPredicate =>
            WhenPredicate is null ? null : Identifiers.Escape(WhenPredicate);

        /// <summary>
        ///     <see cref="UpsertKeyMember" /> as it must be written into emitted C# — a
        ///     <c>[MapCollectionKey]</c> key member, read off both the source and the target element.
        /// </summary>
        public string? EmitUpsertKeyMember =>
            UpsertKeyMember is null ? null : Identifiers.EscapePath(UpsertKeyMember);
    }
}
