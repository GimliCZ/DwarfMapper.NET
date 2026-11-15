// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     Single source of truth for the names of DwarfMapper's generated helper methods. Every synthesized helper
    ///     name is BUILT here and every check that classifies a helper by name (e.g. "is this an object sub-map?")
    ///     goes through here too — so a prefix can never be written in one place and mis-matched (a different literal)
    ///     in another. Historically the collection/dict prefixes (<c>__DwarfMapColl_</c>/<c>__DwarfMapDict_</c>) were
    ///     hand-written to NOT share the common base, and a FlattenGraph check looked for the non-existent
    ///     <c>__DwarfMap_Coll_</c>/<c>__DwarfMap_Dict_</c> — a silent mismatch this type removes.
    /// </summary>
    internal static class GeneratedNames
    {
        /// <summary>
        ///     Root every synthesized helper name starts with — including the collection/dict helpers whose
        ///     names do NOT continue with an underscore (see <see cref="Collection" />). Use this to tell a user method
        ///     from ANY synthesized helper.
        /// </summary>
        public const string Root = "__DwarfMap";

        /// <summary>
        ///     The narrower base for object/scalar/enum/numeric/parsable helpers (Root + <c>_</c>). Collection
        ///     and dictionary helpers are deliberately EXCLUDED (they don't share it) — see <see cref="Collection" />.
        /// </summary>
        public const string Base = Root + "_";

        // ── Base-sharing category prefixes (IsSynthesized covers all of these) ──
        public const string ObjectMap = Base + "Obj_"; // auto-nested object mapper
        public const string Dispatch = Base + "Disp_"; // [MapDerivedType] Preserve dispatch wrapper
        public const string Depth = Base + "Depth_"; // recursion-capable ctx-threaded companion
        public const string Numeric = Base + "Num_"; // checked numeric conversion

        // ── Collection/dict converters INTENTIONALLY do NOT share Base ──
        // A collection/dict converter manages its own null result (empty or null per nullAsNull), so the caller
        // must NOT append the null-forgiving '!' that IsSynthesized(...) drives for object/scalar converters.
        // Keeping these outside Base is exactly what makes IsSynthesized exclude them; IsComplexHelper matches
        // them by their real prefix where they DO need to be recognized (FlattenGraph leaf degradation).
        public const string Collection = "__DwarfMapColl_";
        public const string Dictionary = "__DwarfMapDict_";

        // ── The fast-path collection copies: block copy and SIMD widen, into an array or into a List<T> ──
        // Outside Root altogether, like the two above and for the same reason. They take one argument and do no
        // reference tracking, which is what CopiesACollection is for.
        public const string Blit = "__DwarfBlit_";
        public const string BlitList = "__DwarfBlitL_";
        public const string Widen = "__DwarfWiden_";
        public const string WidenList = "__DwarfWidenL_";

        // The dense enum-keyed dictionary helper ([MapDenseEnumKeys]).
        public const string DenseEnum = "__DwarfDense_";
        public const string UserConv = Base + "UserConv_"; // user-defined conversion operator
        public const string EnumToStr = Base + "EnumStr_"; // enum -> string (by name)
        public const string StrToEnum = Base + "StrEnum_"; // string -> enum (by name)

        // FlattenGraph helper families.
        public const string FlatNode = Base + "FlatNode_";
        public const string FlatNodeDispatch = Base + "FlatNodeDispatch_";
        public const string FlattenGraph = Base + "FlattenGraph_";
        public const string FlattenGraphArr = Base + "FlattenGraphArr_";

        /// <summary>
        ///     True for ANY DwarfMapper-synthesized helper (object/scalar/enum AND collection/dict) — i.e.
        ///     <paramref name="name" /> is NOT a user-declared method. This is the widest classification.
        /// </summary>
        /// <summary>
        ///     Whether <paramref name="name" /> is a helper this generator wrote, of ANY family - as opposed to a method
        ///     the mapper's author declared. Round 35: that includes the families named outside <see cref="Root" />
        ///     (the fast-path collection copies and the dense enum helper). It answered no for those, so a diagnostic
        ///     told the reader their value "goes through '__DwarfWidenL_21d8533...'" as if that were a converter of theirs.
        /// </summary>
        public static bool IsAnySynthesized(string? name)
        {
            return name is not null &&
                   (name.StartsWith(Root, StringComparison.Ordinal) || CopiesACollection(name) || name.StartsWith(DenseEnum, StringComparison.Ordinal));
        }

        /// <summary>
        ///     True for object/scalar/enum/numeric/parsable synthesized helpers — but NOT collection/dict
        ///     (which manage their own null result and must not receive the caller's null-forgiving <c>!</c>).
        /// </summary>
        public static bool IsSynthesized(string? name)
        {
            return name is not null && name.StartsWith(Base, StringComparison.Ordinal);
        }

        /// <summary>True when <paramref name="name" /> is an auto-nested object sub-map (<see cref="ObjectMap" />).</summary>
        public static bool IsObjectMap(string? name)
        {
            return name is not null && name.StartsWith(ObjectMap, StringComparison.Ordinal);
        }

        /// <summary>
        ///     True when <paramref name="name" /> wraps a USER-DEFINED conversion operator
        ///     (<see cref="UserConv" />) — <c>UserConversionConverter</c>'s synthesized shim around an
        ///     <c>implicit</c>/<c>explicit operator</c> the caller wrote. Round 29 T0.2 fix-round-2: a span map's
        ///     blit gate must refuse this specifically — it is the one <see cref="IsSynthesized" />-true prefix
        ///     that can run ARBITRARY caller code, unlike <see cref="ObjectMap" /> (member-by-member, and a span
        ///     map separately checks for pair-scoped customization of that) or the enum arms (byte-identical
        ///     <c>CreateChecked</c>, no customization surface at all).
        /// </summary>
        public static bool IsUserConv(string? name)
        {
            return name is not null && name.StartsWith(UserConv, StringComparison.Ordinal);
        }

        /// <summary>
        ///     True for the "complex" synthesized helpers whose signature may gain <c>(ctx, depth)</c> — object,
        ///     collection, or dictionary maps. Used where a leaf must fall back to topology degradation rather than
        ///     inline such a helper (e.g. FlattenGraph leaves).
        /// </summary>
        /// <summary>
        ///     Whether <paramref name="name" /> is one of the fast-path helpers that copy a whole collection without
        ///     tracking it: a block copy or a widen. Under Preserve such a copy gives every caller its own collection,
        ///     so a <c>[FlattenGraph]</c> leaf must not go through one in silence (DWARF075).
        /// </summary>
        public static bool CopiesACollection(string? name)
        {
            return name is not null &&
                   (name.StartsWith(Blit, StringComparison.Ordinal) || name.StartsWith(BlitList, StringComparison.Ordinal) ||
                    name.StartsWith(Widen, StringComparison.Ordinal) || name.StartsWith(WidenList, StringComparison.Ordinal));
        }

        public static bool IsComplexHelper(string? name)
        {
            return name is not null && (name.StartsWith(ObjectMap, StringComparison.Ordinal) || name.StartsWith(Collection, StringComparison.Ordinal) || name.StartsWith(Dictionary, StringComparison.Ordinal));
        }
    }
}
