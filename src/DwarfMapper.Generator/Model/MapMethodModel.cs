// SPDX-License-Identifier: GPL-2.0-only
using System;
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
    bool IsPreserveMode = false) : IEquatable<MapMethodModel>;
