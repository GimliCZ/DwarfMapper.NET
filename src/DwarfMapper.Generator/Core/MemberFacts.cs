// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Core;

/// <summary>
///     The single implementation of "which members can be read from / written to a type", shared by both
///     engines. It walks the base-type chain (and, for interfaces, all transitively inherited interfaces),
///     de-duplicates by name so a shadowing override yields once, and applies ACCESSOR-level usability rather
///     than merely property-level.
///     <para>
///     This lived privately inside <c>MapperExtractor</c> while <c>MapToGenerator</c> had its own shallower
///     copy that never walked base types — so inherited members were invisible to the registry, silently
///     dropping data. One implementation is what stops that class of divergence recurring.
///     </para>
/// </summary>
internal static class MemberFacts
{
    // A property accessor / field is usable by the generated mapper when it is public, or — when the mapper
    // opted in via [DwarfMapper(AllowNonPublic = true)] — an internal/protected-internal accessor the mapper's
    // assembly can reach (same assembly or via [InternalsVisibleTo]). private/protected stay unreachable.
    internal static bool AccessorUsable(IMethodSymbol? accessor, Compilation? compilation, bool allowNonPublic)
    {
        return accessor is not null &&
               IsMemberReachable(accessor, accessor.DeclaredAccessibility, compilation, allowNonPublic);
    }

    internal static bool FieldUsable(IFieldSymbol field, Compilation? compilation, bool allowNonPublic)
    {
        return IsMemberReachable(field, field.DeclaredAccessibility, compilation, allowNonPublic);
    }

    // public is always reachable; internal / protected-internal is reachable when the mapper opted in AND the
    // mapper's own assembly can see it (same assembly, or [InternalsVisibleTo]). protected / private never are.
    internal static bool IsMemberReachable(ISymbol member, Accessibility accessibility, Compilation? compilation,
        bool allowNonPublic)
    {
        if (accessibility == Accessibility.Public) return true;
        if (!allowNonPublic) return false;
        if (accessibility is not (Accessibility.Internal or Accessibility.ProtectedOrInternal)) return false;
        if (compilation is null) return true; // no context → same-assembly is the only safe assumption
        // Reachable when the member lives in the mapper's own assembly, or its assembly grants
        // [InternalsVisibleTo] to the mapper's assembly. (IsSymbolAccessibleWithin is unreliable for
        // property accessors scoped to an IAssemblySymbol, so check assembly identity / IVT directly.)
        var memberAsm = member.ContainingAssembly;
        return memberAsm is not null
               && (SymbolEqualityComparer.Default.Equals(memberAsm, compilation.Assembly)
                   || memberAsm.GivesAccessTo(compilation.Assembly));
    }

    internal static IEnumerable<(ISymbol Symbol, string Name, ITypeSymbol Type)> Readable(ITypeSymbol type,
        Compilation? compilation = null, bool allowNonPublic = false)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Interface types: walk the interface itself plus all transitively inherited interfaces.
        // Interfaces don't have a BaseType class chain, so the normal loop would only see
        // the interface's own members and miss parent-interface properties.
        if (type.TypeKind == TypeKind.Interface && type is INamedTypeSymbol ifaceType)
        {
            var ifacesToWalk = new[] { type }
                .Concat(ifaceType.AllInterfaces);
            foreach (var iface in ifacesToWalk)
            foreach (var m in iface.GetMembers())
            {
                if (m.IsStatic) continue;
                switch (m)
                {
                    case IPropertySymbol p when !p.IsIndexer && p.GetMethod is not null:
                        if (seen.Add(p.Name))
                            yield return (p, p.Name, p.Type);
                        break;
                    case IFieldSymbol f when !f.IsImplicitlyDeclared:
                        if (seen.Add(f.Name))
                            yield return (f, f.Name, f.Type);
                        break;
                }
            }

            yield break;
        }

        // Classes and structs: walk the inheritance chain.
        for (var current = type;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
            foreach (var m in current.GetMembers())
            {
                if (m.IsStatic) continue;
                switch (m)
                {
                    case IPropertySymbol p
                        when !p.IsIndexer && AccessorUsable(p.GetMethod, compilation, allowNonPublic):
                        if (seen.Add(p.Name)) yield return (p, p.Name, p.Type);
                        break;
                    case IFieldSymbol f when !f.IsImplicitlyDeclared && FieldUsable(f, compilation, allowNonPublic):
                        if (seen.Add(f.Name)) yield return (f, f.Name, f.Type);
                        break;
                }
            }
    }

    internal static IEnumerable<(ISymbol Symbol, string Name, ITypeSymbol Type)> Writable(ITypeSymbol type,
        Compilation? compilation = null, bool allowNonPublic = false)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = type;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
            foreach (var m in current.GetMembers())
            {
                if (m.IsStatic) continue;
                switch (m)
                {
                    case IPropertySymbol p
                        when !p.IsIndexer && AccessorUsable(p.SetMethod, compilation, allowNonPublic):
                        if (seen.Add(p.Name)) yield return (p, p.Name, p.Type);
                        break;
                    case IFieldSymbol f when !f.IsImplicitlyDeclared && !f.IsReadOnly &&
                                             FieldUsable(f, compilation, allowNonPublic):
                        if (seen.Add(f.Name)) yield return (f, f.Name, f.Type);
                        break;
                }
            }
    }
}
