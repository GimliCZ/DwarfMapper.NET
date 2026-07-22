// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

/// <summary>
///     Proves whether two distinct unmanaged structs are byte-identical in layout AND field-name-aligned,
///     so a positional reinterpret equals DwarfMapper's name-based mapping. Recurses through nested structs.
/// </summary>
internal static class BlittableProof
{
    public static bool CanReinterpret(ITypeSymbol src, ITypeSymbol dst)
    {
        // Identity is the existing Clone() memmove, not a reinterpret.
        if (SymbolEqualityComparer.Default.Equals(src, dst)) return false;
        return LayoutIdentical(src, dst);
    }

    /// <summary>
    ///     True when two types are byte-identical in layout AND field-name-aligned, so a positional
    ///     reinterpret equals DwarfMapper's name-based mapping. Recurses through nested structs.
    /// </summary>
    private static bool LayoutIdentical(ITypeSymbol a, ITypeSymbol b)
    {
        if (SymbolEqualityComparer.Default
            .Equals(a, b)) return true; // same type -> trivially same layout and name-aligned
        if (!a.IsUnmanagedType || !b.IsUnmanagedType) return false;
        if (IsPrimitive(a) || IsPrimitive(b))
            return a.SpecialType == b.SpecialType && a.SpecialType != SpecialType.None;
        if (a is not INamedTypeSymbol na || b is not INamedTypeSymbol nb) return false;
        if (na.TypeKind != TypeKind.Struct) return false;
#pragma warning disable CA1508 // flow analysis false positive: INamedTypeSymbol can be Class/Enum/Interface/Delegate, not only Struct
        if (nb.TypeKind !=
            TypeKind.Struct) return false; // excludes enums (TypeKind.Enum): by-name enum mapping != byte copy
#pragma warning restore CA1508
        if (!IsSourceSequential(na, out var packA) || !IsSourceSequential(nb, out var packB)) return false;
        if (packA != packB) return false;

        var fa = InstanceFields(na);
        var fb = InstanceFields(nb);
        if (fa.Count == 0 || fa.Count != fb.Count) return false;
        for (var i = 0; i < fa.Count; i++)
        {
            if (!string.Equals(fa[i].Name, fb[i].Name,
                    StringComparison.Ordinal)) return false; // positional == name-based requires same names
            if (!LayoutIdentical(fa[i].Type,
                    fb[i].Type))
                return false; // recurse: primitive same-SpecialType, identical type, or nested layout-identical struct
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
                fields.Add(f);

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
            if (byFile != 0) return byFile;

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
        if (!t.Locations.Any(l => l.IsInSource)) return false;
        foreach (var attr in t.GetAttributes())
            if (attr.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute")
            {
                if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is int kind &&
                    kind != 0) return false; // 0 = Sequential; 2 = Explicit; 3 = Auto
                foreach (var na in attr.NamedArguments)
                    if (na.Key == "Pack" && na.Value.Value is int p)
                        pack = p;

                return true;
            }

        return true; // no [StructLayout] -> C# struct default is Sequential, Pack 0
    }
}
