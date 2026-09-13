// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Core;

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
    /// <param name="SpanMapBlits">
    ///     When <c>true</c>, the <see cref="IsSpanMap"/> element pair is proven layout-identical
    ///     (<see cref="BlittableProof.CanReinterpret"/> / <see cref="BlittableProof.CanReinterpretEnums"/>), so the
    ///     body is a single <c>MemoryMarshal.Cast&lt;S, D&gt;(src).CopyTo(dst)</c> block copy after the length guard,
    ///     rather than the per-element loop. The element converter resolved into <see cref="Members"/>[0] is left
    ///     unused on this path (still synthesized, for the completeness/diagnostics passes that ask what the pair
    ///     resolves to) — round 29, T0.2.
    /// </param>
    /// <param name="SpanSourceElementFullName">
    ///     The source span's element type, fully qualified — the first type argument to
    ///     <c>MemoryMarshal.Cast&lt;S, D&gt;</c> on a <see cref="SpanMapBlits"/> body.
    /// </param>
    /// <param name="SpanTargetElementFullName">
    ///     The destination span's element type, fully qualified — the second type argument to
    ///     <c>MemoryMarshal.Cast&lt;S, D&gt;</c> on a <see cref="SpanMapBlits"/> body.
    /// </param>
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
    /// <param name="ParameterTypeSignature">
    ///     <see cref="ParameterTypeFullName" /> WITH its nullable reference annotations, for the one place the
    ///     annotation is part of the contract: the source parameter of a signature that must match a partial
    ///     method the USER declared. Null on every model whose signature the generator both writes and calls,
    ///     which then falls back to <see cref="ParameterTypeFullName" />.
    ///     <para>
    ///         It is a SEPARATE field rather than an annotation on <see cref="ParameterTypeFullName" /> because
    ///         that string is not only a signature: it is the operand of <c>typeof(…)</c> in the ambient
    ///         registration, the target of <c>new …()</c> and of a cast, the key the pair is deduplicated and
    ///         resolved by (<c>ResolveByFqn</c>), and text inside diagnostic messages. Annotating it in place was
    ///         measured, not assumed: it turns <c>CS8611</c> into <c>CS8639</c> ("the typeof operator cannot be
    ///         used on a nullable reference type") in the same generated file, and on the return side into
    ///         <c>CS8628</c> ("cannot use a nullable reference type in object creation"). Round 29 task 2.7.
    ///     </para>
    /// </param>
    /// <param name="ReturnTypeSignature">
    ///     The RETURN half of <see cref="ParameterTypeSignature" />, and a separate field for exactly the same
    ///     measured reason: <see cref="ReturnTypeFullName" /> is the pair's canonical identity — the target of the
    ///     <c>new …()</c> the emitter writes, a cast target, the <c>typeof(…)</c> operand and registry key, a dedup
    ///     key and diagnostic message text — so annotating it in place trades <c>CS8611</c> for <c>CS8628</c>
    ///     ("cannot use a nullable reference type in object creation"). Task 2.7 applied that patch, probed it and
    ///     reverted it; this field is what shipped instead. Null on every model whose signature the generator both
    ///     writes and calls, which then falls back to <see cref="ReturnTypeFullName" />.
    ///     <para>
    ///         Read at the three branches that write a RETURN slot which must match a user's own declaration (the
    ///         create-map partial, the async-stream iterator, the returning form of update-into) and by the
    ///         extension facade, whose forwarding method has to declare the same nullability the map it calls
    ///         does — otherwise <c>CS8603</c> lands in <c>DwarfMapper.Extensions.g.cs</c>. The generic case is
    ///         where the compiler is loudest: <c>partial List&lt;Dst?&gt; Many(…)</c> implemented as
    ///         <c>List&lt;Dst&gt;</c> is <c>CS8819</c> plus a <c>CS8619</c> on the returned helper value. A SCALAR
    ///         nullable return is silent at the partial — a stricter return is safe — which is why the one-line
    ///         "annotate the FullName" fix could never have worked for it: its diagnostics were never on the
    ///         signature at all. Round 29 task 2.8.
    ///     </para>
    /// </param>
    /// <param name="UpdateTargetTypeSignature">
    ///     The same annotation-preserving spelling for the DESTINATION PARAMETER of an update-into
    ///     (<c>void Update(S src, T dest)</c>), whose type <see cref="ReturnTypeFullName" /> also holds. It is a
    ///     THIRD field rather than a reuse of <see cref="ReturnTypeSignature" /> because the returning form
    ///     declares two independently-annotated positions from two different symbols: in
    ///     <c>partial Dst Update(Src s, Dst? d)</c> the parameter is annotated and the return is not, and writing
    ///     one string into both slots turns a <c>CS8611</c> on the parameter into a <c>CS8819</c> on the return.
    ///     Null when the model is not an update-into. Round 29 task 2.8.
    /// </param>
    /// <param name="ReturnIsNullableRef">
    ///     Whether the user DECLARED this map to return a nullable reference type. Read only by the ambient
    ///     registration, whose delegate type is the shipped <c>Func&lt;object, object&gt;</c>: a map that may
    ///     return null cannot satisfy that contract, so the emitted lambda coalesces to a loud
    ///     <c>InvalidOperationException</c> naming the pair rather than smuggling a null into a non-nullable
    ///     delegate (<c>CS8603</c>/<c>CS8604</c> in the consumer's <c>.g.cs</c> today). Modelled as a fact rather
    ///     than sniffed out of <see cref="ReturnTypeSignature" />, because the question is about the TOP-LEVEL
    ///     annotation only — <c>List&lt;Dst?&gt;</c> is a non-null list and registers unchanged. Round 29 task 2.8.
    /// </param>
    /// <param name="AsyncStreamTargetElementFullName">
    ///     The destination ELEMENT type of an <see cref="IsAsyncStreamMap" /> method, annotations included — the
    ///     cast the shared <c>CollectionConverter.ElementExpr</c> writes onto the non-null arm of a lifted
    ///     element, so the conditional's type never depends on target-typing. The span map's own
    ///     <see cref="SpanTargetElementFullName" /> is the same thing one endpoint over; this is a separate field
    ///     rather than a reuse of <see cref="ElementTargetTypeFullName" /> because THAT string is a <c>new …</c>
    ///     target in the projection body, where a nullable annotation is <c>CS8628</c>. Empty for every other
    ///     method shape. Round 29 task 2.8.
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
        bool SpanMapBlits = false,
        string SpanSourceElementFullName = "",
        string SpanTargetElementFullName = "",
        bool EmitAsNonPartial = false,
        bool IsAsyncStreamMap = false,
        string? AsyncCancellationParam = null,
        EquatableArray<string> ExtraParameters = default,
        bool ParameterIsPublicType = false,
        bool ReturnIsPublicType = false,
        string? FactoryMethod = null,
        bool Withheld = false,
        string? ParameterTypeSignature = null,
        string? ReturnTypeSignature = null,
        string? UpdateTargetTypeSignature = null,
        bool ReturnIsNullableRef = false,
        string AsyncStreamTargetElementFullName = "") : IEquatable<MapMethodModel>
    {
        /// <summary><see cref="MethodName" /> as it must be written into emitted C#.</summary>
        /// <remarks>
        ///     <para>
        ///         The raw field stays, and stays raw, because it is this method's IDENTITY as much as its
        ///         spelling: <see cref="MemberMap.ConverterMethod" /> is matched against it by ordinal equality
        ///         to build the call graph, the DWARF060 same-source collision pass keys on it, the depth
        ///         companion is <c>GeneratedNames.Depth + MethodName</c>, and
        ///         <c>GeneratedNames.IsObjectMap(MethodName)</c> asks whether the generator synthesized it.
        ///         Escaping at construction would make every one of those comparisons miss for exactly the
        ///         consumer whose method is called <c>@class</c>.
        ///     </para>
        ///     <para>
        ///         <see cref="Identifiers.Escape" /> and not <c>EscapeTypeName</c>: a method may be called
        ///         <c>record</c> or <c>partial</c> with no escape at all, and widening would churn every golden.
        ///     </para>
        /// </remarks>
        public string EmitMethodName => Identifiers.Escape(MethodName);

        /// <summary><see cref="ParameterName" /> as it must be written into emitted C#.</summary>
        /// <remarks>
        ///     The raw field is read by the extra-parameter matcher and by DWARF047's unused-parameter check,
        ///     which compare against <c>IParameterSymbol.Name</c> and must keep seeing the unescaped spelling.
        /// </remarks>
        public string EmitParameterName => Identifiers.Escape(ParameterName);

        /// <summary><see cref="UpdateTargetParameterName" /> as it must be written into emitted C#.</summary>
        public string EmitUpdateTargetParameterName => Identifiers.Escape(UpdateTargetParameterName);

        /// <summary><see cref="SpanTargetParameterName" /> as it must be written into emitted C#.</summary>
        public string EmitSpanTargetParameterName => Identifiers.Escape(SpanTargetParameterName);

        /// <summary>
        ///     <see cref="AsyncCancellationParam" /> as it must be written into emitted C#, or <c>null</c> when
        ///     the user declared no token parameter.
        /// </summary>
        public string? EmitAsyncCancellationParam =>
            AsyncCancellationParam is null ? null : Identifiers.Escape(AsyncCancellationParam);

        /// <summary>
        ///     <see cref="FactoryMethod" /> as it must be written into emitted C#, or <c>null</c> when the pair
        ///     constructs its destination itself.
        /// </summary>
        public string? EmitFactoryMethod =>
            FactoryMethod is null ? null : Identifiers.Escape(FactoryMethod);

        /// <summary>
        ///     The parameter type as written into a signature that must MATCH the user's declaration:
        ///     <see cref="ParameterTypeSignature" /> (annotations kept) when the model carries one,
        ///     <see cref="ParameterTypeFullName" /> otherwise.
        /// </summary>
        /// <remarks>
        ///     One statement of the fallback for every emit site that writes a user-matched signature. Declared,
        ///     async-stream and update-into models always carry the signature; synthesized entries do not. So the
        ///     fallback is reached through the sites that meet both, not repeated where only one shape arrives.
        /// </remarks>
        public string EmitParameterTypeSignature => ParameterTypeSignature ?? ParameterTypeFullName;

        /// <summary>
        ///     The ELEMENT member of an element-wise method (the async-stream and span maps), or a member with no
        ///     converter and no null handling when the model carries none, so readers take its fields without a null
        ///     test at each one.
        /// </summary>
        /// <remarks>
        ///     Async-stream and span models are always built with exactly one element member, so the "none" answer is reached
        ///     here, where the unit test asks it, rather than as a null-conditional at every field the emitter reads.
        ///     It emits exactly what the old null-conditionals did: no converter, <see cref="NullHandling.None" />, and
        ///     every flag false.
        /// </remarks>
        public MemberMap ElementMember => Members.Count > 0 ? Members[0] : NoElementMember;

        private static readonly MemberMap NoElementMember = new("", "");

        /// <summary>The return-slot twin of <see cref="EmitParameterTypeSignature" />.</summary>
        public string EmitReturnTypeSignature => ReturnTypeSignature ?? ReturnTypeFullName;

        /// <summary>
        ///     The update-into DESTINATION parameter's twin of <see cref="EmitParameterTypeSignature" />: its
        ///     <see cref="UpdateTargetTypeSignature" />, or <see cref="ReturnTypeFullName" /> (which holds the same type)
        ///     when the model carries none.
        /// </summary>
        public string EmitUpdateTargetTypeSignature => UpdateTargetTypeSignature ?? ReturnTypeFullName;

        /// <summary>
        ///     <see cref="BeforeHooks" /> as they must be written into emitted C# — a <c>[BeforeMap]</c> method
        ///     the consumer named <c>@class</c> is called by name from the generated body.
        /// </summary>
        public IEnumerable<string> EmitBeforeHooks => BeforeHooks.Select(Identifiers.Escape);
    }
}
