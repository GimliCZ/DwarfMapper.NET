// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Pipeline;

internal enum NullStrategy
{
    Throw = 0,
    SetDefault = 1,
}

internal static class MapperExtractor
{
    public static MapperClassModel Extract(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var classSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
        var classSyntax = (ClassDeclarationSyntax)ctx.TargetNode;
        var diagnostics = new List<DiagnosticInfo>();

        var isPartial = classSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        if (!isPartial)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.MapperNotPartial,
                LocationInfo.From(classSyntax.Identifier.GetLocation()),
                classSymbol.Name));
        }

        var classIgnores = ReadIgnores(classSymbol);
        var caseInsensitive = ReadCaseInsensitive(ctx.Attributes);
        var enumStrategy = ReadEnumStrategy(ctx.Attributes);
        var nullStrategy = ReadNullStrategy(ctx.Attributes);
        var synthesized = new Dictionary<string, SynthesizedMethod>(System.StringComparer.Ordinal);
        var allMethods = CollectMethods(classSymbol);
        var mapperMethods = CollectMapperMethods(classSymbol);
        var (beforeHookDefs, afterHookDefs) = CollectHooks(classSymbol, diagnostics);
        var methods = new List<MapMethodModel>();

        foreach (var method in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            ct.ThrowIfCancellationRequested();

            if (method.MethodKind != MethodKind.Ordinary || !method.IsPartialDefinition)
            {
                continue;
            }

            var methodLocation = LocationInfo.From(method.Locations.FirstOrDefault() ?? Location.None);

            if (method.ReturnsVoid || method.Parameters.Length != 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod, methodLocation, method.Name));
                continue;
            }

            if (method.ReturnType is not INamedTypeSymbol targetType)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod, methodLocation, method.Name));
                continue;
            }

            var sourceType = method.Parameters[0].Type;

            if (!HasAccessibleParameterlessCtor(targetType))
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.NoParameterlessConstructor, methodLocation, targetType.Name));
                continue;
            }

            var ignores = new HashSet<string>(classIgnores);
            foreach (var i in ReadIgnores(method))
            {
                ignores.Add(i);
            }

            var explicitMaps = ReadExplicitMaps(method);
            var flattenRoots = ReadFlattenRoots(method);

            var members = ResolveMembers(
                sourceType, targetType, ignores, ctx.SemanticModel.Compilation,
                methodLocation, diagnostics, caseInsensitive, explicitMaps, allMethods, mapperMethods,
                enumStrategy, synthesized, nullStrategy, flattenRoots);

            var applicableBefore = new List<string>();
            foreach (var h in beforeHookDefs)
            {
                if (HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.ParamType))
                {
                    applicableBefore.Add(h.Name);
                }
            }
            var applicableAfter = new List<HookCall>();
            foreach (var h in afterHookDefs)
            {
                if (h.P1 is null)
                {
                    if (HasImplicitConversion(ctx.SemanticModel.Compilation, targetType, h.P0))
                    {
                        applicableAfter.Add(new HookCall(h.Name, false));
                    }
                }
                else if (HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.P0)
                    && HasImplicitConversion(ctx.SemanticModel.Compilation, targetType, h.P1))
                {
                    applicableAfter.Add(new HookCall(h.Name, true));
                }
            }

            methods.Add(new MapMethodModel(
                method.Name,
                AccessibilityText(method.DeclaredAccessibility),
                targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                method.Parameters[0].Name,
                sourceType.IsReferenceType,
                EquatableArray.From(members),
                EquatableArray.From(applicableBefore),
                EquatableArray.From(applicableAfter)));
        }

        return new MapperClassModel(
            classSymbol.ContainingNamespace.IsGlobalNamespace ? "" : classSymbol.ContainingNamespace.ToDisplayString(),
            classSymbol.Name,
            AccessibilityText(classSymbol.DeclaredAccessibility),
            EquatableArray.From(methods),
            EquatableArray.From(diagnostics),
            EquatableArray.From(synthesized.Values.OrderBy(m => m.Name, System.StringComparer.Ordinal)));
    }

    private static List<MemberMap> ResolveMembers(
        ITypeSymbol sourceType, INamedTypeSymbol targetType, HashSet<string> ignores,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        bool caseInsensitive, IReadOnlyList<(string Source, string Target, string? Use)> explicitMaps,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
        EnumStrategy enumStrategy, Dictionary<string, SynthesizedMethod> synthesized,
        NullStrategy nullStrategy, IReadOnlyList<string> flattenRoots)
    {
        var comparer = caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;

        var sourceGroups = ReadableMembers(sourceType)
            .GroupBy(m => m.Name, comparer)
            .ToDictionary(g => g.Key, g => g.ToList(), comparer);

        var writableByName = new Dictionary<string, ITypeSymbol>(System.StringComparer.Ordinal);
        foreach (var m in WritableMembers(targetType))
        {
            writableByName[m.Name] = m.Type;
        }

        var result = new List<MemberMap>();
        var handledTargets = new HashSet<string>(System.StringComparer.Ordinal);

        var comparerForLeaves = comparer; // same comparer used for member matching
        var flattenInfos = new List<(string Root, IReadOnlyList<(string Name, ITypeSymbol Type)> Leaves)>();
        foreach (var root in flattenRoots)
        {
            var match = ReadableMembers(sourceType)
                .Where(m => comparerForLeaves.Equals(m.Name, root))
                .Select(m => ((string Name, ITypeSymbol Type)?)m)
                .FirstOrDefault();
            if (match is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.FlattenRootInvalid, location, root));
                continue;
            }
            var rootType = match.Value.Type;
            // Scalars (string, primitives, enums) are not flattenable roots — flattening their
            // BCL members (e.g. string.Length) is never intended and must not happen silently.
            if (rootType.SpecialType != SpecialType.None || rootType.TypeKind == TypeKind.Enum)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.FlattenRootInvalid, location, root));
                continue;
            }
            var leaves = ReadableMembers(rootType).ToList();
            if (leaves.Count == 0)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.FlattenRootInvalid, location, root));
                continue;
            }
            flattenInfos.Add((match.Value.Name, leaves));
        }

        // EXPLICIT: [MapProperty] pairs take precedence and are matched by exact name.
        var explicitSeen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var (srcName, tgtName, useMethod) in explicitMaps)
        {
            if (!explicitSeen.Add(tgtName))
            {
                // More than one [MapProperty] for the same destination.
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DuplicateMapProperty, location, tgtName));
                continue;
            }

            handledTargets.Add(tgtName);

            if (ignores.Contains(tgtName))
            {
                // Contradictory: [MapIgnore] and [MapProperty] target the same member.
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IgnoreExplicitConflict, location, tgtName));
                continue;
            }

            if (!writableByName.TryGetValue(tgtName, out var tgtType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownTarget, location, tgtName));
                continue;
            }

            var srcMatch = ReadableMembers(sourceType)
                .Where(m => System.StringComparer.Ordinal.Equals(m.Name, srcName))
                .Select(m => (ITypeSymbol?)m.Type)
                .FirstOrDefault();
            if (srcMatch is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, location, srcName));
                continue;
            }

            if (TryResolveConversion(compilation, srcMatch, tgtType, useMethod, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, tgtName, diagnostics, out var conv, out var nullH))
            {
                result.Add(new MemberMap(tgtName, srcName, conv, nullH));
            }
        }

        // AUTO: remaining writable targets matched by name under the comparer.
        var targets = WritableMembers(targetType)
            .OrderBy(m => m.Name, System.StringComparer.Ordinal)
            .ToList();
        foreach (var target in targets)
        {
            if (handledTargets.Contains(target.Name) || ignores.Contains(target.Name))
            {
                continue;
            }

            if (!sourceGroups.TryGetValue(target.Name, out var matches))
            {
                var flatMatches = new List<(string Root, string Leaf, ITypeSymbol LeafType)>();
                foreach (var fi in flattenInfos)
                {
                    foreach (var leaf in fi.Leaves)
                    {
                        if (comparer.Equals(leaf.Name, target.Name))
                        {
                            flatMatches.Add((fi.Root, leaf.Name, leaf.Type));
                        }
                    }
                }

                if (flatMatches.Count > 1)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousFlatten, location, target.Name));
                    continue;
                }
                if (flatMatches.Count == 1)
                {
                    var fm = flatMatches[0];
                    if (TryResolveConversion(compilation, fm.LeafType, target.Type, null, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, target.Name, diagnostics, out var fconv, out var fnull))
                    {
                        result.Add(new MemberMap(target.Name, fm.Root + "." + fm.Leaf, fconv, fnull));
                    }
                    continue;
                }

                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnmappedMember, location, target.Name));
                continue;
            }

            if (matches.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousMatch, location, target.Name));
                continue;
            }

            var source = matches[0];
            if (TryResolveConversion(compilation, source.Type, target.Type, null, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, target.Name, diagnostics, out var conv, out var nullH))
            {
                result.Add(new MemberMap(target.Name, source.Name, conv, nullH));
            }
        }

        // READ-ONLY destinations with a matching source (silent-loss guard).
        foreach (var readOnly in ReadOnlyMembers(targetType).OrderBy(m => m.Name, System.StringComparer.Ordinal))
        {
            if (handledTargets.Contains(readOnly.Name) || ignores.Contains(readOnly.Name))
            {
                continue;
            }
            if (sourceGroups.ContainsKey(readOnly.Name))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReadOnlyDestinationMember, location, readOnly.Name));
            }
        }

        return result;
    }

    private static bool TryResolveConversion(
        Compilation compilation, ITypeSymbol srcType, ITypeSymbol tgtType, string? useMethod,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
        EnumStrategy enumStrategy, Dictionary<string, SynthesizedMethod> synthesized,
        NullStrategy nullStrategy,
        LocationInfo? location, string targetName, List<DiagnosticInfo> diagnostics,
        out string? converterMethod, out Model.NullHandling nullHandling)
    {
        converterMethod = null;
        nullHandling = Model.NullHandling.None;

        if (useMethod is not null)
        {
            foreach (var m in allMethods)
            {
                if (string.Equals(m.Name, useMethod, System.StringComparison.Ordinal)
                    && HasImplicitConversion(compilation, srcType, m.ParamType)
                    && HasImplicitConversion(compilation, m.ReturnType, tgtType))
                {
                    converterMethod = m.Name;
                    return true;
                }
            }
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UseMethodInvalid, location, useMethod));
            return false;
        }

        if (CollectionConverter.TryResolve(srcType, tgtType, out var srcElem, out var tgtElem, out var collShape))
        {
            if (!TryResolveConversion(compilation, srcElem, tgtElem, null, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, targetName, diagnostics, out var elemConv, out var elemNull))
            {
                return false; // element diagnostic already reported by the recursive call
            }
            converterMethod = CollectionConverter.Synthesize(synthesized, srcType, srcElem, tgtElem, collShape, elemConv, elemNull);
            return true;
        }

        if (HasImplicitConversion(compilation, srcType, tgtType))
        {
            return true; // direct assignment
        }

        if (IsNullableValue(srcType, out var underlying) && HasImplicitConversion(compilation, underlying, tgtType))
        {
            nullHandling = nullStrategy == NullStrategy.SetDefault ? Model.NullHandling.ValueOrDefault : Model.NullHandling.ThrowIfNull;
            return true;
        }

        string? found = null;
        foreach (var c in autoCandidates)
        {
            if (HasImplicitConversion(compilation, srcType, c.ParamType)
                && HasImplicitConversion(compilation, c.ReturnType, tgtType))
            {
                if (found is not null)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConversion, location, targetName));
                    return false;
                }
                found = c.Name;
            }
        }

        if (found is not null)
        {
            converterMethod = found;
            return true;
        }

        var enumMethod = EnumConverter.TryCreate(srcType, tgtType, enumStrategy, synthesized, location, targetName, diagnostics);
        if (enumMethod is not null)
        {
            converterMethod = enumMethod;
            return true;
        }

        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoImplicitConversion, location, targetName));
        return false;
    }

    private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> CollectMethods(INamedTypeSymbol classSymbol)
    {
        var methods = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>();
        foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (m.MethodKind == MethodKind.Ordinary && !m.ReturnsVoid && m.Parameters.Length == 1)
            {
                methods.Add((m.Name, m.Parameters[0].Type, m.ReturnType));
            }
        }
        return methods;
    }

    private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> CollectMapperMethods(INamedTypeSymbol classSymbol)
    {
        var methods = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>();
        foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (m.MethodKind == MethodKind.Ordinary && m.IsPartialDefinition
                && !m.ReturnsVoid && m.Parameters.Length == 1 && m.ReturnType is INamedTypeSymbol)
            {
                methods.Add((m.Name, m.Parameters[0].Type, m.ReturnType));
            }
        }
        return methods;
    }

    private static (List<(string Name, ITypeSymbol ParamType)> Before, List<(string Name, ITypeSymbol P0, ITypeSymbol? P1)> After)
        CollectHooks(INamedTypeSymbol classSymbol, List<DiagnosticInfo> diagnostics)
    {
        var before = new List<(string Name, ITypeSymbol ParamType)>();
        var after = new List<(string Name, ITypeSymbol P0, ITypeSymbol? P1)>();
        foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            var isBefore = m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "DwarfMapper.BeforeMapAttribute");
            var isAfter = m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "DwarfMapper.AfterMapAttribute");
            if (!isBefore && !isAfter)
            {
                continue;
            }
            var loc = LocationInfo.From(m.Locations.FirstOrDefault() ?? Location.None);
            if (!m.ReturnsVoid)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidHook, loc, m.Name));
                continue;
            }
            if (isBefore)
            {
                if (m.Parameters.Length != 1)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidHook, loc, m.Name));
                }
                else
                {
                    before.Add((m.Name, m.Parameters[0].Type));
                }
            }
            if (isAfter)
            {
                if (m.Parameters.Length == 1)
                {
                    after.Add((m.Name, m.Parameters[0].Type, null));
                }
                else if (m.Parameters.Length == 2)
                {
                    after.Add((m.Name, m.Parameters[0].Type, m.Parameters[1].Type));
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidHook, loc, m.Name));
                }
            }
        }
        return (before, after);
    }

    private static bool HasImplicitConversion(Compilation compilation, ITypeSymbol source, ITypeSymbol target)
    {
        var conversion = ((CSharpCompilation)compilation).ClassifyConversion(source, target);
        return conversion.IsImplicit && !conversion.IsUserDefined;
    }

    private static bool IsNullableValue(ITypeSymbol type, out ITypeSymbol underlying)
    {
        if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            underlying = named.TypeArguments[0];
            return true;
        }
        underlying = type;
        return false;
    }

    private static IEnumerable<(string Name, ITypeSymbol Type)> ReadableMembers(ITypeSymbol type)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var m in current.GetMembers())
            {
                if (m.IsStatic)
                {
                    continue;
                }
                switch (m)
                {
                    case IPropertySymbol p when !p.IsIndexer && p.GetMethod is { DeclaredAccessibility: Accessibility.Public }:
                        if (seen.Add(p.Name))
                        {
                            yield return (p.Name, p.Type);
                        }
                        break;
                    case IFieldSymbol f when !f.IsImplicitlyDeclared && f.DeclaredAccessibility == Accessibility.Public:
                        if (seen.Add(f.Name))
                        {
                            yield return (f.Name, f.Type);
                        }
                        break;
                }
            }
        }
    }

    private static IEnumerable<(string Name, ITypeSymbol Type)> WritableMembers(ITypeSymbol type)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var m in current.GetMembers())
            {
                if (m.IsStatic)
                {
                    continue;
                }
                switch (m)
                {
                    case IPropertySymbol p when !p.IsIndexer && p.SetMethod is { DeclaredAccessibility: Accessibility.Public }:
                        if (seen.Add(p.Name))
                        {
                            yield return (p.Name, p.Type);
                        }
                        break;
                    case IFieldSymbol f when !f.IsImplicitlyDeclared && !f.IsReadOnly && f.DeclaredAccessibility == Accessibility.Public:
                        if (seen.Add(f.Name))
                        {
                            yield return (f.Name, f.Type);
                        }
                        break;
                }
            }
        }
    }

    private static IEnumerable<(string Name, ITypeSymbol Type)> ReadOnlyMembers(ITypeSymbol type)
    {
        var writable = new HashSet<string>(WritableMembers(type).Select(m => m.Name), System.StringComparer.Ordinal);
        return ReadableMembers(type).Where(m => !writable.Contains(m.Name));
    }

    private static bool HasAccessibleParameterlessCtor(INamedTypeSymbol type) =>
        type.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

    private static IEnumerable<string> ReadIgnores(ISymbol symbol) =>
        symbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == "DwarfMapper.MapIgnoreAttribute")
            .Select(a => a.ConstructorArguments.Length == 1 ? a.ConstructorArguments[0].Value as string : null)
            .Where(s => s is not null)
            .Select(s => s!);

    private static List<(string Source, string Target, string? Use)> ReadExplicitMaps(ISymbol method)
    {
        var maps = new List<(string Source, string Target, string? Use)>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != "DwarfMapper.MapPropertyAttribute")
            {
                continue;
            }
            if (attr.ConstructorArguments.Length == 2
                && attr.ConstructorArguments[0].Value is string s
                && attr.ConstructorArguments[1].Value is string t)
            {
                string? use = null;
                foreach (var na in attr.NamedArguments)
                {
                    if (na.Key == "Use" && na.Value.Value is string u)
                    {
                        use = u;
                    }
                }
                maps.Add((s, t, use));
            }
        }
        return maps;
    }

    private static List<string> ReadFlattenRoots(ISymbol method)
    {
        var roots = new List<string>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "DwarfMapper.FlattenAttribute"
                && attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is string s)
            {
                roots.Add(s);
            }
        }
        return roots;
    }

    private static EnumStrategy ReadEnumStrategy(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "EnumStrategy" && named.Value.Value is int i)
                {
                    return (EnumStrategy)i;
                }
            }
        }
        return EnumStrategy.ByName;
    }

    private static NullStrategy ReadNullStrategy(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "NullStrategy" && named.Value.Value is int i)
                {
                    return (NullStrategy)i;
                }
            }
        }
        return NullStrategy.Throw;
    }

    private static bool ReadCaseInsensitive(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "CaseInsensitive" && named.Value.Value is bool b)
                {
                    return b;
                }
            }
        }
        return false;
    }

    private static string AccessibilityText(Accessibility a) => a switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.Private => "private",
        _ => "public",
    };
}
