// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper;

/// <summary>
/// Controls how a null source collection/dictionary is mapped.
/// </summary>
public enum NullCollectionStrategy
{
    /// <summary>
    /// A null source collection produces an empty target collection (the default).
    /// The mapper never throws a <see cref="System.NullReferenceException"/> for
    /// a null collection source.
    /// </summary>
    AsEmpty = 0,

    /// <summary>
    /// A null source collection propagates as null on the target (the target
    /// member must then be nullable — a nullable reference or a nullable value-type collection like ImmutableArray&lt;T&gt;?; a non-nullable target silently degrades to AsEmpty).  Re-enumeration of a lazy
    /// <c>IEnumerable&lt;T&gt;</c> target re-maps the source on each call.
    /// </summary>
    AsNull = 1,
}
