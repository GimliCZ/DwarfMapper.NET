// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

/// <summary>
/// Symbol-level predicates against the (.NET 10) Compilation.
/// Pure functions; no state is stored in cached models.
/// </summary>
internal static class TypeInterfaces
{
    /// <summary>
    /// Returns true for the eight core integral types plus nint/nuint.
    /// Enums have SpecialType.None — this returns false for them.
    /// </summary>
    internal static bool IsIntegral(ITypeSymbol t) => t.SpecialType is
        SpecialType.System_SByte or SpecialType.System_Byte or
        SpecialType.System_Int16 or SpecialType.System_UInt16 or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or
        SpecialType.System_Int64 or SpecialType.System_UInt64 or
        SpecialType.System_IntPtr or SpecialType.System_UIntPtr;

    /// <summary>
    /// Returns true when <paramref name="t"/> implements <c>IParsable&lt;T&gt;</c>
    /// (i.e. the self-referential implementation <c>T : IParsable&lt;T&gt;</c>).
    /// Guards null in case the compilation does not have System.IParsable`1 (older TFMs).
    /// </summary>
    internal static bool ImplementsIParsable(Compilation compilation, ITypeSymbol t)
    {
        var parsableOpen = compilation.GetTypeByMetadataName("System.IParsable`1");
        if (parsableOpen is null)
            return false;

        return t.AllInterfaces.Any(i =>
            SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, parsableOpen)
            && i.TypeArguments.Length == 1
            && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], t));
    }

    /// <summary>
    /// Returns true when <paramref name="t"/> implements <c>System.IFormattable</c>.
    /// </summary>
    internal static bool ImplementsIFormattable(ITypeSymbol t) =>
        t.AllInterfaces.Any(i =>
            i.ToDisplayString() == "System.IFormattable");

    /// <summary>
    /// Returns true for the integral set (sufficient for our generic-math scope).
    /// Also walks AllInterfaces for robustness on exotic types, but the SpecialType
    /// short-circuit is authoritative for all built-in integral types.
    /// </summary>
    internal static bool IsINumberBase(Compilation compilation, ITypeSymbol t)
    {
        if (IsIntegral(t))
            return true;

        var numberBaseOpen = compilation.GetTypeByMetadataName("System.Numerics.INumberBase`1");
        if (numberBaseOpen is null)
            return false;

        return t.AllInterfaces.Any(i =>
            SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, numberBaseOpen));
    }
}
