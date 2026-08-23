// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     Proves whether two distinct unmanaged structs are byte-identical in layout AND field-name-aligned,
    ///     so a positional reinterpret equals DwarfMapper's name-based mapping. Recurses through nested structs.
    /// </summary>
    internal static class BlittableProof
    {
        public static bool CanReinterpret(ITypeSymbol src, ITypeSymbol dst)
        {
            // Identity is the existing Clone() memmove, not a reinterpret.
            if (SymbolEqualityComparer.Default.Equals(src, dst))
            {
                return false;
            }

            return LayoutIdentical(src, dst);
        }

        /// <summary>
        ///     Explains why a pair that a caller could reasonably have expected to blit did not, but ONLY for a
        ///     genuine near-miss: a pair blocked by exactly one identifiable thing.
        ///     <para>
        ///         Deliberately narrow. The broad reading — "report whenever something looked blittable" — would
        ///         fire on every ordinary struct-array mapping whose members happen to differ, which is not a
        ///         failed fast path but simply a mapping. An informational diagnostic that common gets suppressed
        ///         wholesale, and would then hide the cases worth reading. So a pair whose field counts or field
        ///         TYPES differ is not reported at all: nothing about it suggests the caller expected a blit.
        ///     </para>
        ///     <para>
        ///         The name-mismatch case is the one this exists for. Such a pair is byte-identical and one rename
        ///         away from the fast path, and nothing in the build would otherwise say so.
        ///     </para>
        /// </summary>
        public static bool TryExplainNearMiss(ITypeSymbol src, ITypeSymbol dst, out string reason)
        {
            reason = string.Empty;

            // Identity already takes the Clone() memmove; there is no fast path being missed.
            if (SymbolEqualityComparer.Default.Equals(src, dst))
            {
                return false;
            }

            // Two different primitives are a CONVERSION, not a near-miss blit. Same-primitive is identity, above.
            if (IsPrimitive(src) || IsPrimitive(dst))
            {
                return false;
            }

            if (src is not INamedTypeSymbol a || dst is not INamedTypeSymbol b)
            {
                return false;
            }

            if (a.TypeKind != TypeKind.Struct)
            {
                return false; // enums and classes are not almost-blittable; they are something else
            }
#pragma warning disable CA1508 // flow analysis false positive: INamedTypeSymbol can be Class/Enum/Interface/Delegate, not only Struct
            if (b.TypeKind != TypeKind.Struct)
            {
                return false;
            }
#pragma warning restore CA1508

            if (!a.IsUnmanagedType || !b.IsUnmanagedType)
            {
                return false; // a managed member is a categorical refusal, not a near-miss
            }

            // SHAPE FIRST, blockers second — the order matters and the obvious order is wrong.
            //
            // Checking the layout blockers first looks natural (it is the order the proof itself uses) and
            // silently breaks the scoping rule: EVERY pair of distinct metadata structs would report, however
            // unrelated, because the metadata branch would answer before anything examined their fields. Note
            // that `decimal` is not in IsPrimitive, so `decimal` against `Guid` is a real reachable pair — two
            // structs with nothing in common, which would have been announced as "nearly layout-identical".
            //
            // GetMembers() works perfectly well on metadata symbols; the in-source restriction exists because
            // an absent [StructLayout] is only reliable for a type we can see the source of, not because the
            // fields are unavailable. So the shape check runs first for every pair, and a pair that is not
            // shaped alike stays silent whatever else is true of it.
            var fa = InstanceFields(a);
            var fb = InstanceFields(b);
            if (fa.Count == 0 || fa.Count != fb.Count)
            {
                return false; // a different shape entirely — an ordinary mapping, not a missed fast path
            }

            // Types must line up positionally for this to be a near-miss at all; if they do not, the pair is
            // simply two different structs and the element loop is the right answer.
            for (var i = 0; i < fa.Count; i++)
                if (!LayoutIdentical(fa[i].Type, fb[i].Type))
                {
                    return false;
                }

            // Only now, with the shapes known to align, is there a fast path worth explaining the absence of.
            if (!a.Locations.Any(l => l.IsInSource))
            {
                reason = $"'{a.Name}' is declared in metadata, so an absent [StructLayout] cannot be read as Sequential";
                return true;
            }

            if (!b.Locations.Any(l => l.IsInSource))
            {
                reason = $"'{b.Name}' is declared in metadata, so an absent [StructLayout] cannot be read as Sequential";
                return true;
            }

            if (!IsSourceSequential(a, out var packA))
            {
                reason = $"'{a.Name}' declares a [StructLayout] that is not Sequential, so its field order is not guaranteed";
                return true;
            }

            if (!IsSourceSequential(b, out var packB))
            {
                reason = $"'{b.Name}' declares a [StructLayout] that is not Sequential, so its field order is not guaranteed";
                return true;
            }

            if (packA != packB)
            {
                reason = $"'{a.Name}' packs to {packA} and '{b.Name}' packs to {packB}, so the two layouts differ";
                return true;
            }

            for (var i = 0; i < fa.Count; i++)
                if (!string.Equals(fa[i].Name, fb[i].Name, StringComparison.Ordinal))
                {
                    reason =
                        $"field {i} is named '{fa[i].Name}' on '{a.Name}' but '{fb[i].Name}' on '{b.Name}', and a " +
                        "positional reinterpret would only agree with DwarfMapper's by-name mapping if the names line up";
                    return true;
                }

            return false; // nothing left to block it — it would have been proven, so there is nothing to explain
        }

        /// <summary>
        ///     True when two types are byte-identical in layout AND field-name-aligned, so a positional
        ///     reinterpret equals DwarfMapper's name-based mapping. Recurses through nested structs.
        /// </summary>
        private static bool LayoutIdentical(ITypeSymbol a, ITypeSymbol b)
        {
            if (SymbolEqualityComparer.Default
                .Equals(a, b))
            {
                return true; // same type -> trivially same layout and name-aligned
            }

            if (!a.IsUnmanagedType || !b.IsUnmanagedType)
            {
                return false;
            }

            if (IsPrimitive(a) || IsPrimitive(b))
            {
                return a.SpecialType == b.SpecialType && a.SpecialType != SpecialType.None;
            }

            if (a is not INamedTypeSymbol na || b is not INamedTypeSymbol nb)
            {
                return false;
            }

            if (na.TypeKind != TypeKind.Struct)
            {
                return false;
            }
#pragma warning disable CA1508 // flow analysis false positive: INamedTypeSymbol can be Class/Enum/Interface/Delegate, not only Struct
            if (nb.TypeKind !=
                TypeKind.Struct)
            {
                return false; // excludes enums (TypeKind.Enum): by-name enum mapping != byte copy
            }
#pragma warning restore CA1508
            if (!IsSourceSequential(na, out var packA) || !IsSourceSequential(nb, out var packB))
            {
                return false;
            }

            if (packA != packB)
            {
                return false;
            }

            var fa = InstanceFields(na);
            var fb = InstanceFields(nb);
            if (fa.Count == 0 || fa.Count != fb.Count)
            {
                return false;
            }

            for (var i = 0; i < fa.Count; i++)
            {
                if (!string.Equals(fa[i].Name,
                        fb[i].Name,
                        StringComparison.Ordinal))
                {
                    return false; // positional == name-based requires same names
                }

                if (!LayoutIdentical(fa[i].Type,
                        fb[i].Type))
                {
                    return false; // recurse: primitive same-SpecialType, identical type, or nested layout-identical struct
                }
            }

            return true;
        }

        private static bool IsPrimitive(ITypeSymbol t)
        {
            return t.SpecialType is
                SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte
                or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_IntPtr
                or SpecialType.System_UIntPtr
                or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Char;
        }

        private static List<IFieldSymbol> InstanceFields(INamedTypeSymbol t)
        {
            var fields = new List<IFieldSymbol>();
            foreach (var m in t.GetMembers())
                if (m is IFieldSymbol f && !f.IsStatic && !f.IsConst)
                {
                    fields.Add(f);
                }

            // GetMembers() order is declaration order, which for a struct split across PARTIAL files depends on the
            // order the compiler happened to see the files. The blit proof compares fields POSITIONALLY, so an
            // unstable order can flip a struct pair between "provably blittable" and "not" from build to build.
            // (It cannot make an unsafe ACCEPT: for a sequential-layout struct the declaration order is also the
            // emitted layout, so a reordering is a genuine layout change. This is purely about determinism.)
            fields.Sort((a, b) =>
            {
                var pathA = a.Locations.Length > 0 ? a.Locations[0].SourceTree?.FilePath ?? string.Empty : string.Empty;
                var pathB = b.Locations.Length > 0 ? b.Locations[0].SourceTree?.FilePath ?? string.Empty : string.Empty;
                var byFile = string.CompareOrdinal(pathA, pathB);
                if (byFile != 0)
                {
                    return byFile;
                }

                var posA = a.Locations.Length > 0 ? a.Locations[0].SourceSpan.Start : 0;
                var posB = b.Locations.Length > 0 ? b.Locations[0].SourceSpan.Start : 0;
                return posA.CompareTo(posB);
            });

            return fields;
        }

        private static bool IsSourceSequential(INamedTypeSymbol t, out int pack)
        {
            pack = 0;
            // Auto-blit requires a source struct so that an absent [StructLayout] reliably means the C# default (Sequential).
            if (!t.Locations.Any(l => l.IsInSource))
            {
                return false;
            }

            foreach (var attr in t.GetAttributes())
                if (attr.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute")
                {
                    if (attr.ConstructorArguments.Length >= 1 &&
                        attr.ConstructorArguments[0].Value is int kind &&
                        kind != 0)
                    {
                        return false; // 0 = Sequential; 2 = Explicit; 3 = Auto
                    }

                    foreach (var na in attr.NamedArguments)
                        if (na.Key == "Pack" && na.Value.Value is int p)
                        {
                            pack = p;
                        }

                    return true;
                }

            return true; // no [StructLayout] -> C# struct default is Sequential, Pack 0
        }
    }
}
