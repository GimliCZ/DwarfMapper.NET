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
    bool ReturnIsReferenceType = true) : IEquatable<MapMethodModel>;
