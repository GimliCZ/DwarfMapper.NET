// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

/// <summary>
/// Proves whether two distinct unmanaged structs are byte-identical in layout AND field-name-aligned,
/// so a positional reinterpret equals DwarfMapper's name-based mapping. v1: PRIMITIVES-ONLY — every
/// field must be a primitive (no nested structs); nested-struct recursion is a later relaxation.
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
        if (!src.IsUnmanagedType || !dst.IsUnmanagedType)
        {
            return false;
        }
        // Both must be plain (non-enum) structs (excludes enums and primitives at the top level).
        if (src is not INamedTypeSymbol ns || dst is not INamedTypeSymbol nd)
        {
            return false;
        }
        if (ns.TypeKind != TypeKind.Struct)
        {
            return false;
        }
#pragma warning disable CA1508 // flow analysis false positive: INamedTypeSymbol can be Class/Enum/Interface/Delegate, not only Struct
        if (nd.TypeKind != TypeKind.Struct)
        {
            return false;
        }
#pragma warning restore CA1508
        if (!IsSourceSequential(ns, out var packA) || !IsSourceSequential(nd, out var packB))
        {
            return false;
        }
        if (packA != packB)
        {
            return false;
        }

        var fa = InstanceFields(ns);
        var fb = InstanceFields(nd);
        if (fa.Count == 0 || fa.Count != fb.Count)
        {
            return false;
        }
        for (var i = 0; i < fa.Count; i++)
        {
            if (!string.Equals(fa[i].Name, fb[i].Name, System.StringComparison.Ordinal))
            {
                return false; // positional == name-based requires same names
            }
            // PRIMITIVES-ONLY (v1): each field must be a primitive of the same special type.
            if (!IsPrimitive(fa[i].Type) || fa[i].Type.SpecialType != fb[i].Type.SpecialType)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsPrimitive(ITypeSymbol t) => t.SpecialType is
        SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte
        or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
        or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_IntPtr or SpecialType.System_UIntPtr
        or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Char;

    private static List<IFieldSymbol> InstanceFields(INamedTypeSymbol t)
    {
        var fields = new List<IFieldSymbol>();
        foreach (var m in t.GetMembers())
        {
            if (m is IFieldSymbol f && !f.IsStatic && !f.IsConst)
            {
                fields.Add(f);
            }
        }
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
        {
            if (attr.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute")
            {
                if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is int kind && kind != 0)
                {
                    return false; // 0 = Sequential; 2 = Explicit; 3 = Auto
                }
                foreach (var na in attr.NamedArguments)
                {
                    if (na.Key == "Pack" && na.Value.Value is int p)
                    {
                        pack = p;
                    }
                }
                return true;
            }
        }
        return true; // no [StructLayout] -> C# struct default is Sequential, Pack 0
    }
}
