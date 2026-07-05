// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;

namespace DwarfMapper.Generator.Model;

/// <summary>A single partial mapping method to implement.</summary>
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
    /// <summary>
    /// <c>true</c> for user-declared partial methods (emitted as <c>public partial T Name(S s)</c>);
    /// <c>false</c> for auto-synthesized nested methods (emitted as <c>private T Name(S s)</c>).
    /// </summary>
    bool IsPartial = true,
    /// <summary>
    /// Whether the return (target) type is a reference type. Controls the synthesized
    /// null-guard: a reference return can <c>return null!</c>; a value-type return cannot,
    /// so it throws instead (avoids CS0037 on a struct/record-struct nested target).
    /// </summary>
    bool ReturnIsReferenceType = true,
    /// <summary>
    /// When <c>true</c>, this synthesized method is on a type-graph cycle and must be
    /// emitted with the depth-guarded signature <c>(S s, DwarfRefContext ctx, int depth)</c>.
    /// The public declared mapper creates a <see cref="global::DwarfMapper.DwarfRefContext"/>
    /// and passes <c>ctx, 0</c> into the first tracked call.
    /// False for acyclic pairs (zero overhead).
    /// </summary>
    bool IsRecursionCapable = false,
    /// <summary>
    /// The <c>MaxDepth</c> value configured on the mapper class (default 64).
    /// Only used when <see cref="IsRecursionCapable"/> is true for the public entry method.
    /// </summary>
    int MaxDepth = 64,
    /// <summary>
    /// When <c>true</c>, <c>ReferenceHandling = Preserve</c> is active for this mapper class.
    /// Recursion-capable synthesized methods switch from single-expression construction to the
    /// register-before-populate multi-statement form:
    /// <code>
    ///   var __dwarf_t = new T(...);
    ///   ctx.SetReference(s, __dwarf_t);
    ///   __dwarf_t.Member1 = ...; __dwarf_t.Member2 = ...;
    ///   return __dwarf_t;
    /// </code>
    /// The public entry method creates <c>DwarfRefContext(maxDepth, preserve: true)</c>.
    /// Non-recursion-capable pairs and None mode are UNCHANGED.
    /// </summary>
    bool IsPreserveMode = false,
    /// <summary>
    /// For projection methods (<see cref="IsProjection"/> = true), the inline expression
    /// fragments for each member (nested new / Select / ctor / direct assign).
    /// When <see cref="ProjectionMembers"/> has exactly one entry with an empty
    /// <see cref="ProjectionMemberMap.TargetName"/>, the single <see cref="ProjectionMemberMap.InlineExpr"/>
    /// is the entire lambda body (constructor-only projection).
    /// Empty for non-projection methods.
    /// </summary>
    EquatableArray<ProjectionMemberMap> ProjectionMembers = default,
    /// <summary>
    /// Resolved [FlattenGraph] directives for this method.
    /// Empty for methods without [FlattenGraph] annotations.
    /// Stored for snapshot/documentation; the emitter generates code via injected
    /// <see cref="MemberMap"/> entries whose <see cref="MemberMap.ConverterMethod"/> is the
    /// synthesized traversal or array-wrapper helper.
    /// </summary>
    EquatableArray<FlattenGraphDirective> FlattenGraphDirectives = default,
    /// <summary>
    /// When <c>true</c>, this is a top-level partial method whose return type is a
    /// collection or dictionary. The method body is emitted as:
    /// <code>return __converter_helper__(param);</code>
    /// where the single entry in <see cref="Members"/> carries the synthesized helper name
    /// in <see cref="MemberMap.ConverterMethod"/> and <see cref="MemberMap.SourceName"/> == ""
    /// (sentinel: source is the raw parameter).
    /// The normal object-construction path (<c>return new T(...) { ... }</c>) is NOT used.
    /// </summary>
    bool IsTopLevelCollectionConversion = false,
    /// <summary>
    /// Sorted (most-derived-first) arms for a derived-type dispatch method.
    /// Non-empty when the method carries [MapDerivedType] annotations.
    /// </summary>
    EquatableArray<DerivedTypeArm> DerivedTypeArms = default,
    /// <summary>
    /// When <c>true</c>, <c>ReferenceHandling = None</c> with <c>OnCycle = SetNull</c> is active.
    /// Recursion-capable reference pairs wrap their body in an on-stack guard
    /// (<c>DwarfRefContext.TryEnterNode</c>/<c>ExitNode</c>): a re-entrant back-edge to a node
    /// already on the active mapping stack returns <c>null</c>, breaking the cycle with a finite
    /// acyclic projection. The public entry creates <c>DwarfRefContext(maxDepth, setNull: true)</c>.
    /// Mutually exclusive with <see cref="IsPreserveMode"/> (Preserve ignores OnCycle).
    /// </summary>
    bool IsSetNullMode = false,
    /// <summary>
    /// When <c>true</c>, this is an update-into-existing method <c>void/T Map(S src, T dest)</c>:
    /// settable members of the existing <see cref="UpdateTargetParameterName"/> instance are assigned
    /// from <see cref="ParameterName"/> (no construction; target identity preserved). The body emits
    /// null-guards for both parameters, then <c>dest.Member = …;</c> per member.
    /// </summary>
    bool IsUpdateInto = false,
    /// <summary>The name of the destination parameter for an <see cref="IsUpdateInto"/> method.</summary>
    string UpdateTargetParameterName = "",
    /// <summary>When <c>true</c>, the <see cref="IsUpdateInto"/> method returns <c>void</c>;
    /// otherwise it returns the (same) destination instance.</summary>
    bool UpdateReturnsVoid = false,
    /// <summary>
    /// When <c>true</c>, this is a zero-alloc span map <c>void Map(ReadOnlySpan&lt;S&gt; src, Span&lt;D&gt; dst)</c>:
    /// elements are mapped <c>dst[i] = conv(src[i])</c> into the caller buffer (no allocation), with a
    /// defensive length check (destination too small → <c>ArgumentException</c>).
    /// <see cref="ParameterTypeFullName"/> is the source span type, <see cref="ReturnTypeFullName"/> the
    /// destination span type, <see cref="ParameterName"/> the source param, and
    /// <see cref="SpanTargetParameterName"/> the destination param. The single element conversion is in
    /// <see cref="Members"/>[0] (<see cref="MemberMap.ConverterMethod"/> = element converter or null for a
    /// direct/implicit element assignment).
    /// </summary>
    bool IsSpanMap = false,
    /// <summary>The destination span parameter name for an <see cref="IsSpanMap"/> method.</summary>
    string SpanTargetParameterName = "",
    /// <summary>
    /// When <c>true</c>, this is an attribute-declared mapper (<c>[GenerateMap&lt;S,T&gt;]</c>) emitted as a
    /// FULL <c>public</c> method rather than a <c>partial</c> implementation — the user did not declare a
    /// partial method, so the generated file provides the whole method. Treated like <see cref="IsPartial"/>
    /// for all body logic (it is a public entry); only the signature omits the <c>partial</c> keyword.
    /// </summary>
    bool EmitAsNonPartial = false,
    /// <summary>
    /// When <c>true</c>, this is an async streaming map
    /// <c>IAsyncEnumerable&lt;D&gt; Map(IAsyncEnumerable&lt;S&gt; src)</c>: emitted as an
    /// <c>async</c> iterator (<c>await foreach … yield return conv(item)</c>) that lazily transforms
    /// the source sequence element-by-element. <see cref="ParameterTypeFullName"/> is the source
    /// <c>IAsyncEnumerable&lt;S&gt;</c>, <see cref="ReturnTypeFullName"/> the destination
    /// <c>IAsyncEnumerable&lt;D&gt;</c>, and the element conversion is <see cref="Members"/>[0].
    /// </summary>
    bool IsAsyncStreamMap = false,
    /// <summary>
    /// Additional source-like parameters declared after the source (e.g. <c>Dto Map(Entity e, string
    /// tenant)</c>), each pre-formatted as <c>"global::Type name"</c> for direct signature emission. They
    /// are matched to destination members by name (precedence: explicit &gt; extra parameter &gt; by-name
    /// member) and are deliberately NOT propagated into nested mappings.
    /// </summary>
    EquatableArray<string> ExtraParameters = default,
    /// <summary>
    /// Whether the source (parameter) type is effectively public (itself and every containing type). Used only
    /// by the convenience facade to decide whether the generated per-target extension (e.g.
    /// <c>order.ToOrderDto()</c>) may be emitted <c>public</c> (cross-assembly) without an
    /// inconsistent-accessibility error. Defaults to <c>false</c>.
    /// </summary>
    bool ParameterIsPublicType = false,
    /// <summary>Whether the destination (return) type is effectively public — see <see cref="ParameterIsPublicType"/>.</summary>
    bool ReturnIsPublicType = false,
    /// <summary>
    /// When non-null, names a factory method on the mapper that constructs the destination from the source
    /// (pair-scoped <c>[MapConstructor&lt;S,T&gt;]</c> / AutoMapper <c>ConstructUsing</c>). The body becomes
    /// <c>var __dwarf_target = Factory(src); __dwarf_target.M = …; return __dwarf_target;</c> — construction is
    /// delegated to the factory and only settable members in <see cref="Members"/> are assigned afterward
    /// (<see cref="ConstructorArguments"/> is empty; <c>init</c>/<c>required</c>/get-only members are the
    /// factory's responsibility).
    /// </summary>
    string? FactoryMethod = null) : IEquatable<MapMethodModel>;
