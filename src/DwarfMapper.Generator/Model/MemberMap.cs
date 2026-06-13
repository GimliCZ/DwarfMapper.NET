// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>
/// One resolved destination&lt;-source member assignment. <see cref="ConverterMethod"/>,
/// when set, transforms the value; otherwise <see cref="NullHandling"/> controls
/// how a nullable value-type source is unwrapped.
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
    string? ValueExpression = null) : System.IEquatable<MemberMap>;
