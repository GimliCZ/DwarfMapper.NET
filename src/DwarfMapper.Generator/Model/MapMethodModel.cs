// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;

namespace DwarfMapper.Generator.Model
{
    /// <summary>A single partial mapping method to implement.</summary>
    /// <param name="IsPartial">
    ///     <c>true</c> for user-declared partial methods (emitted as <c>public partial T Name(S s)</c>);
    ///     <c>false</c> for auto-synthesized nested methods (emitted as <c>private T Name(S s)</c>).
    /// </param>
    /// <param name="ReturnIsReferenceType">
    ///     Whether the return (target) type is a reference type. Controls the synthesized
    ///     null-guard: a reference return can <c>return null!</c>; a value-type return cannot,
    ///     so it throws instead (avoids CS0037 on a struct/record-struct nested target).
    /// </param>
    /// <param name="IsRecursionCapable">
    ///     When <c>true</c>, this synthesized method is on a type-graph cycle and must be
    ///     emitted with the depth-guarded signature <c>(S s, DwarfRefContext ctx, int depth)</c>.
    ///     The public declared mapper creates a <c>DwarfMapper.DwarfRefContext</c>
    ///     and passes <c>ctx, 0</c> into the first tracked call.
    ///     False for acyclic pairs (zero overhead).
    /// </param>
    /// <param name="MaxDepth">
    ///     The <c>MaxDepth</c> value configured on the mapper class (default 64).
    ///     Only used when <see cref="IsRecursionCapable"/> is true for the public entry method.
    /// </param>
    /// <param name="IsPreserveMode">
    ///     When <c>true</c>, <c>ReferenceHandling = Preserve</c> is active for this mapper class.
    ///     Recursion-capable synthesized methods switch from single-expression construction to the
    ///     register-before-populate multi-statement form:
    ///     <code>
    ///       var __dwarf_t = new T(...);
    ///       ctx.SetReference(s, __dwarf_t);
    ///       __dwarf_t.Member1 = ...; __dwarf_t.Member2 = ...;
    ///       return __dwarf_t;
    ///     </code>
    ///     The public entry method creates <c>DwarfRefContext(maxDepth, preserve: true)</c>.
    ///     Non-recursion-capable pairs and None mode are UNCHANGED.
    /// </param>
    /// <param name="ProjectionMembers">
    ///     For projection methods (<see cref="IsProjection"/> = true), the inline expression
    ///     fragments for each member (nested new / Select / ctor / direct assign).
    ///     When the FIRST entry has an empty <see cref="ProjectionMemberMap.TargetName"/>, its
    ///     <see cref="ProjectionMemberMap.InlineExpr"/> is a <c>new T(…)</c> constructor call and any entries
    ///     after it are members the constructor did not take, emitted as an object initializer on top of it.
    ///     Otherwise every entry is a member-init binding.
    ///     Empty for non-projection methods.
    /// </param>
    /// <param name="FlattenGraphDirectives">
    ///     Resolved [FlattenGraph] directives for this method.
    ///     Empty for methods without [FlattenGraph] annotations.
    ///     Stored for snapshot/documentation; the emitter generates code via injected
    ///     <see cref="MemberMap"/> entries whose <see cref="MemberMap.ConverterMethod"/> is the
    ///     synthesized traversal or array-wrapper helper.
    /// </param>
    /// <param name="IsTopLevelCollectionConversion">
    ///     When <c>true</c>, this is a top-level partial method whose return type is a
    ///     collection or dictionary. The method body is emitted as:
    ///     <code>return __converter_helper__(param);</code>
    ///     where the single entry in <see cref="Members"/> carries the synthesized helper name
    ///     in <see cref="MemberMap.ConverterMethod"/> and <see cref="MemberMap.SourceName"/> == ""
    ///     (sentinel: source is the raw parameter).
    ///     The normal object-construction path (<c>return new T(...) { ... }</c>) is NOT used.
    /// </param>
    /// <param name="DerivedTypeArms">
    ///     Sorted (most-derived-first) arms for a derived-type dispatch method.
    ///     Non-empty when the method carries [MapDerivedType] annotations.
    /// </param>
    /// <param name="IsSetNullMode">
    ///     When <c>true</c>, <c>ReferenceHandling = None</c> with <c>OnCycle = SetNull</c> is active.
    ///     Recursion-capable reference pairs wrap their body in an on-stack guard
    ///     (<c>DwarfRefContext.TryEnterNode</c>/<c>ExitNode</c>): a re-entrant back-edge to a node
    ///     already on the active mapping stack returns <c>null</c>, breaking the cycle with a finite
    ///     acyclic projection. The public entry creates <c>DwarfRefContext(maxDepth, setNull: true)</c>.
    ///     Mutually exclusive with <see cref="IsPreserveMode"/> (Preserve ignores OnCycle).
    /// </param>
    /// <param name="IsUpdateInto">
    ///     When <c>true</c>, this is an update-into-existing method <c>void/T Map(S src, T dest)</c>:
    ///     settable members of the existing <see cref="UpdateTargetParameterName"/> instance are assigned
    ///     from <see cref="ParameterName"/> (no construction; target identity preserved). The body emits
    ///     null-guards for both parameters, then <c>dest.Member = …;</c> per member.
    /// </param>
    /// <param name="UpdateTargetParameterName">The name of the destination parameter for an <see cref="IsUpdateInto"/> method.</param>
    /// <param name="UpdateReturnsVoid">
    ///     When <c>true</c>, the <see cref="IsUpdateInto"/> method returns <c>void</c>;
    ///     otherwise it returns the (same) destination instance.
    /// </param>
    /// <param name="IsSpanMap">
    ///     When <c>true</c>, this is a zero-alloc span map <c>void Map(ReadOnlySpan&lt;S&gt; src, Span&lt;D&gt; dst)</c>:
    ///     elements are mapped <c>dst[i] = conv(src[i])</c> into the caller buffer (no allocation), with a
    ///     defensive length check (destination too small → <c>ArgumentException</c>).
    ///     <see cref="ParameterTypeFullName"/> is the source span type, <see cref="ReturnTypeFullName"/> the
    ///     destination span type, <see cref="ParameterName"/> the source param, and
    ///     <see cref="SpanTargetParameterName"/> the destination param. The single element conversion is in
    ///     <see cref="Members"/>[0] (<see cref="MemberMap.ConverterMethod"/> = element converter or null for a
    ///     direct/implicit element assignment).
    /// </param>
    /// <param name="SpanTargetParameterName">The destination span parameter name for an <see cref="IsSpanMap"/> method.</param>
    /// <param name="EmitAsNonPartial">
    ///     When <c>true</c>, this is an attribute-declared mapper (<c>[GenerateMap&lt;S,T&gt;]</c>) emitted as a
    ///     FULL <c>public</c> method rather than a <c>partial</c> implementation — the user did not declare a
    ///     partial method, so the generated file provides the whole method. Treated like <see cref="IsPartial"/>
    ///     for all body logic (it is a public entry); only the signature omits the <c>partial</c> keyword.
    /// </param>
    /// <param name="IsAsyncStreamMap">
    ///     When <c>true</c>, this is an async streaming map
    ///     <c>IAsyncEnumerable&lt;D&gt; Map(IAsyncEnumerable&lt;S&gt; src)</c>: emitted as an
    ///     <c>async</c> iterator (<c>await foreach … yield return conv(item)</c>) that lazily transforms
    ///     the source sequence element-by-element. <see cref="ParameterTypeFullName"/> is the source
    ///     <c>IAsyncEnumerable&lt;S&gt;</c>, <see cref="ReturnTypeFullName"/> the destination
    ///     <c>IAsyncEnumerable&lt;D&gt;</c>, and the element conversion is <see cref="Members"/>[0].
    /// </param>
    /// <param name="AsyncCancellationParam">
    ///     For an <see cref="IsAsyncStreamMap"/> method, the name of a user-declared
    ///     <c>CancellationToken</c> parameter, or null when none was declared. The generated half must match the
    ///     user's partial signature exactly, so the token can only exist if the user asked for it; when present the
    ///     emitted parameter carries <c>[EnumeratorCancellation]</c> and the loop threads it through
    ///     <c>WithCancellation</c>, which is the only way cancellation can reach a consumer's
    ///     <c>await foreach</c>.
    /// </param>
    /// <param name="ExtraParameters">
    ///     Additional source-like parameters declared after the source (e.g. <c>Dto Map(Entity e, string
    ///     tenant)</c>), each pre-formatted as <c>"global::Type name"</c> for direct signature emission. They
    ///     are matched to destination members by name (precedence: explicit &gt; extra parameter &gt; by-name
    ///     member) and are deliberately NOT propagated into nested mappings.
    /// </param>
    /// <param name="ParameterIsPublicType">
    ///     Whether the source (parameter) type is effectively public (itself and every containing type). Used only
    ///     by the convenience facade to decide whether the generated per-target extension (e.g.
    ///     <c>order.ToOrderDto()</c>) may be emitted <c>public</c> (cross-assembly) without an
    ///     inconsistent-accessibility error. Defaults to <c>false</c>.
    /// </param>
    /// <param name="ReturnIsPublicType">Whether the destination (return) type is effectively public — see <see cref="ParameterIsPublicType"/>.</param>
    /// <param name="FactoryMethod">
    ///     When non-null, names a factory method on the mapper that constructs the destination from the source
    ///     (pair-scoped <c>[MapConstructor&lt;S,T&gt;]</c> / AutoMapper <c>ConstructUsing</c>). The body becomes
    ///     <c>var __dwarf_target = Factory(src); __dwarf_target.M = …; return __dwarf_target;</c> — construction is
    ///     delegated to the factory and only settable members in <see cref="Members"/> are assigned afterward
    ///     (<see cref="ConstructorArguments"/> is empty; <c>init</c>/<c>required</c>/get-only members are the
    ///     factory's responsibility).
    /// </param>
    /// <param name="Withheld">
    ///     When <c>true</c>, this method was RESOLVED but must not be EMITTED: its own completeness gate
    ///     refused it (<c>DWARF001</c>), and I17 confines that refusal to the method rather than killing the
    ///     mapper. The model is still built and still sits in <see cref="MapperClassModel.Methods" />, because
    ///     every class-level analysis downstream — the DWARF060 same-source collision pass, <c>[RestatesBase]</c>
    ///     drift, recursion capability, the location table keyed by index into that list — asks what the mapper
    ///     DECLARES, and a declaration is not un-made by failing to compile. Only the six emission and
    ///     aggregation sites skip it, so the single thing that changes is what reaches the consumer's file.
    /// </param>
    public sealed record MapMethodModel(
        string MethodName,
        string Accessibility,
        string ReturnTypeFullName,
        string ParameterTypeFullName,
        string ParameterName,
        bool ParameterIsReferenceType,
        EquatableArray<MemberMap> Members,
        EquatableArray<string> BeforeHooks,
        EquatableArray<HookCall> AfterHooks,
        bool IsProjection,
        string ElementTargetTypeFullName,
        EquatableArray<MemberMap> ConstructorArguments = default,
        bool IsPartial = true,
        bool ReturnIsReferenceType = true,
        bool IsRecursionCapable = false,
        int MaxDepth = 64,
        bool IsPreserveMode = false,
        EquatableArray<ProjectionMemberMap> ProjectionMembers = default,
        EquatableArray<FlattenGraphDirective> FlattenGraphDirectives = default,
        bool IsTopLevelCollectionConversion = false,
        EquatableArray<DerivedTypeArm> DerivedTypeArms = default,
        bool IsSetNullMode = false,
        bool IsUpdateInto = false,
        string UpdateTargetParameterName = "",
        bool UpdateReturnsVoid = false,
        bool IsSpanMap = false,
        string SpanTargetParameterName = "",
        bool EmitAsNonPartial = false,
        bool IsAsyncStreamMap = false,
        string? AsyncCancellationParam = null,
        EquatableArray<string> ExtraParameters = default,
        bool ParameterIsPublicType = false,
        bool ReturnIsPublicType = false,
        string? FactoryMethod = null,
        bool Withheld = false) : IEquatable<MapMethodModel>;
}
