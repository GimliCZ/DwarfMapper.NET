// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Pipeline;

/// <summary>
/// Single source of truth for the names of DwarfMapper's generated helper methods. Every synthesized helper
/// name is BUILT here and every check that classifies a helper by name (e.g. "is this an object sub-map?")
/// goes through here too — so a prefix can never be written in one place and mis-matched (a different literal)
/// in another. Historically the collection/dict prefixes (<c>__DwarfMapColl_</c>/<c>__DwarfMapDict_</c>) were
/// hand-written to NOT share the common base, and a FlattenGraph check looked for the non-existent
/// <c>__DwarfMap_Coll_</c>/<c>__DwarfMap_Dict_</c> — a silent mismatch this type removes.
/// </summary>
internal static class GeneratedNames
{
    /// <summary>Common base every synthesized helper name starts with.</summary>
    public const string Base = "__DwarfMap_";

    // ── Base-sharing category prefixes (IsSynthesized covers all of these) ──
    public const string ObjectMap   = Base + "Obj_";        // auto-nested object mapper
    public const string Dispatch    = Base + "Disp_";        // [MapDerivedType] Preserve dispatch wrapper
    public const string Depth       = Base + "Depth_";       // recursion-capable ctx-threaded companion
    public const string Numeric     = Base + "Num_";         // checked numeric conversion

    // ── Collection/dict converters INTENTIONALLY do NOT share Base ──
    // A collection/dict converter manages its own null result (empty or null per nullAsNull), so the caller
    // must NOT append the null-forgiving '!' that IsSynthesized(...) drives for object/scalar converters.
    // Keeping these outside Base is exactly what makes IsSynthesized exclude them; IsComplexHelper matches
    // them by their real prefix where they DO need to be recognized (FlattenGraph leaf degradation).
    public const string Collection  = "__DwarfMapColl_";
    public const string Dictionary  = "__DwarfMapDict_";
    public const string UserConv    = Base + "UserConv_";    // user-defined conversion operator
    public const string EnumToStr   = Base + "EnumStr_";     // enum -> string (by name)
    public const string StrToEnum   = Base + "StrEnum_";     // string -> enum (by name)

    // FlattenGraph helper families.
    public const string FlatNode          = Base + "FlatNode_";
    public const string FlatNodeDispatch  = Base + "FlatNodeDispatch_";
    public const string FlattenGraph      = Base + "FlattenGraph_";
    public const string FlattenGraphArr   = Base + "FlattenGraphArr_";

    /// <summary>True when <paramref name="name"/> is any DwarfMapper-synthesized helper (not a user method).</summary>
    public static bool IsSynthesized(string? name) =>
        name is not null && name.StartsWith(Base, StringComparison.Ordinal);

    /// <summary>True when <paramref name="name"/> is an auto-nested object sub-map (<see cref="ObjectMap"/>).</summary>
    public static bool IsObjectMap(string? name) =>
        name is not null && name.StartsWith(ObjectMap, StringComparison.Ordinal);

    /// <summary>True for the "complex" synthesized helpers whose signature may gain <c>(ctx, depth)</c> — object,
    /// collection, or dictionary maps. Used where a leaf must fall back to topology degradation rather than
    /// inline such a helper (e.g. FlattenGraph leaves).</summary>
    public static bool IsComplexHelper(string? name) =>
        name is not null
        && (name.StartsWith(ObjectMap, StringComparison.Ordinal)
            || name.StartsWith(Collection, StringComparison.Ordinal)
            || name.StartsWith(Dictionary, StringComparison.Ordinal));
}
