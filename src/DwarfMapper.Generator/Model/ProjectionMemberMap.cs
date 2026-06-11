// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>
/// A single projection member binding for IQueryable.Select — carries the
/// target name and a complete inline expression fragment (no helper calls).
/// Value-equatable; safe for incremental-generator model caching.
/// </summary>
public sealed record ProjectionMemberMap(
    /// <summary>
    /// The destination member name (used as the LHS of member-init).
    /// Empty string ("") signals a constructor-only projection where
    /// <see cref="InlineExpr"/> is the entire lambda body expression
    /// (e.g. "new global::D.DstRec(x: __s.X, y: __s.Y)").
    /// </summary>
    string TargetName,
    /// <summary>
    /// The complete RHS inline expression, e.g.:
    ///   "__s.Age"
    ///   "(global::D.Status2)__s.Status"
    ///   "__s.Inner == null ? null : new global::D.InnerDto { A = __s.Inner.A }"
    ///   "__s.Items.Select(__i0 => new global::D.ItemDto { V = __i0.V }).ToList()"
    ///   "new global::D.PointDto(x: __s.Point.X, y: __s.Point.Y)"
    /// Never contains a synthesized helper call (__DwarfMap_*).
    /// </summary>
    string InlineExpr) : System.IEquatable<ProjectionMemberMap>;
