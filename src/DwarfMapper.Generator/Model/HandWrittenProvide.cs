// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Model;

/// <summary>
///     A hand-written method the author marked <c>[ProvidesMap]</c>, to be registered into the ambient
///     registry alongside the generated maps.
/// </summary>
/// <remarks>
///     Exists because some conversions are legitimately not mapping SHAPES — an object that holds a collection
///     mapped to the collection itself being the common one. Those are written by hand and are then ordinary
///     methods, so nothing registers them: every facade call site for them throws, and a parity harness
///     reports them as "not registered" when the code is in fact fine. Round 18 hit this on five pairs.
/// </remarks>
/// <param name="MethodName">The method to call.</param>
/// <param name="SourceTypeFullName">Its parameter type, fully qualified.</param>
/// <param name="TargetTypeFullName">Its return type, fully qualified.</param>
/// <param name="IsStatic">Whether it is called on the type rather than on a cached instance.</param>
public sealed record HandWrittenProvide(
    string MethodName,
    string SourceTypeFullName,
    string TargetTypeFullName,
    bool IsStatic) : IEquatable<HandWrittenProvide>;
