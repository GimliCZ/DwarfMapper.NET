// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Pipeline;

internal static partial class MapperExtractor
{
    private static List<string> ReadReinterpretMembers(ISymbol method)
    {
        var members = new List<string>();
        foreach (var attr in method.GetAttributes())
            if (attr.AttributeClass?.ToDisplayString() == KnownNames.ReinterpretFqn
                && attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is string m)
                members.Add(m);

        return members;
    }

    private static EnumStrategy ReadEnumStrategy(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "EnumStrategy" && named.Value.Value is int i)
                return (EnumStrategy)i;

        return EnumStrategy.ByName;
    }

    private static NullStrategy ReadNullStrategy(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "NullStrategy" && named.Value.Value is int i)
                return (NullStrategy)i;

        return NullStrategy.Throw;
    }

    private static bool ReadCaseInsensitive(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "CaseInsensitive" && named.Value.Value is bool b)
                return b;

        return false;
    }

    /// <summary>
    ///     Reads the class-level <see cref="DwarfMapper.DwarfMapperAttribute.GenerateExtensions" /> value
    ///     from the <c>[DwarfMapper]</c> attribute. Defaults to <c>true</c> (the convenience facade is opt-out).
    /// </summary>
    private static bool ReadGenerateExtensions(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "GenerateExtensions" && named.Value.Value is bool b)
                return b;

        return true;
    }

    /// <summary>
    ///     True when <paramref name="t" /> and every type that contains it are declared <c>public</c> — i.e. it is
    ///     reachable from another assembly. Used to gate <c>public</c> facade extensions (a public extension over a
    ///     non-public type is CS0051) and ambient-registry registration (a cross-assembly map must name both types).
    ///     It inspects the type and its containing-type chain, unwraps arrays to their element type, and recurses
    ///     into generic type arguments — so e.g. <c>ICollection&lt;Internal&gt;</c> / <c>Internal[]</c> are NOT
    ///     effectively public, while <c>ICollection&lt;PublicDto&gt;</c> is.
    /// </summary>
    private static bool IsEffectivelyPublic(ITypeSymbol t)
    {
        if (t is IArrayTypeSymbol arr)
            return IsEffectivelyPublic(arr.ElementType);

        for (ISymbol? s = t; s is not null and not INamespaceSymbol; s = s.ContainingSymbol)
            if (s.DeclaredAccessibility != Accessibility.Public)
                return false;

        if (t is INamedTypeSymbol named)
            foreach (var typeArgument in named.TypeArguments)
                if (!IsEffectivelyPublic(typeArgument))
                    return false;

        return true;
    }

    /// <summary>
    ///     Reads <c>[DwarfMapper(MaxDepth = N)]</c>; defaults to 64; clamps to [1, 1000].
    /// </summary>
    private static int ReadMaxDepth(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "MaxDepth" && named.Value.Value is int i)
            {
                // Clamp to [1, 1000] — matches DwarfRefContext.AbsoluteMaxDepth
                if (i < 1) return 1;
                if (i > 1000) return 1000;
                return i;
            }

        return 64; // default
    }

    /// <summary>
    ///     Reads the class-level <see cref="DwarfMapper.DwarfMapperAttribute.AutoNest" /> value
    ///     from the <c>[DwarfMapper]</c> attribute. Defaults to <c>true</c>.
    /// </summary>
    private static bool ReadAutoNest(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "AutoNest" && named.Value.Value is bool b)
                return b;

        return true; // default: auto-nesting enabled
    }

    /// <summary>
    ///     Reads the class-level <see cref="DwarfMapper.DwarfMapperAttribute.AutoMatchMembers" /> value.
    ///     Defaults to <c>true</c>. When <c>false</c> the mapper is explicit-only (the trust-boundary guard) and
    ///     nothing is auto-wired by name — see <see cref="DiagnosticDescriptors.AutoMatchDisabled" />.
    /// </summary>
    private static bool ReadAutoMatchMembers(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "AutoMatchMembers" && named.Value.Value is bool b)
                return b;

        return true; // default: by-name auto-matching enabled
    }

    /// <summary>
    ///     Reads the class-level <see cref="DwarfMapper.DwarfMapperAttribute.IgnoreObsoleteMembers" /> value.
    ///     Defaults to <c>false</c>.
    /// </summary>
    private static bool ReadIgnoreObsoleteMembers(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "IgnoreObsoleteMembers" && named.Value.Value is bool b)
                return b;

        return false;
    }

    /// <summary>
    ///     Names of the accessible instance properties/fields of <paramref name="type" /> that carry
    ///     <c>[System.ObsoleteAttribute]</c> — the members <c>IgnoreObsoleteMembers</c> drops from mapping.
    ///     Walks the inheritance chain so an obsolete member declared on a base type is included too.
    /// </summary>
    private static IEnumerable<string> ObsoleteMemberNames(ITypeSymbol type)
    {
        for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
            foreach (var member in t.GetMembers())
                if (member is IPropertySymbol or IFieldSymbol && IsObsolete(member))
                    yield return member.Name;
    }

    private static bool IsObsolete(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
            if (attribute.AttributeClass is { Name: "ObsoleteAttribute" } a
                && a.ContainingNamespace?.ToDisplayString() == "System")
                return true;

        return false;
    }

    /// <summary>
    ///     Reads the class-level <see cref="DwarfMapper.DwarfMapperAttribute.SkipNullSourceMembers" /> value.
    ///     Defaults to <c>false</c>.
    /// </summary>
    private static bool ReadSkipNullSourceMembers(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "SkipNullSourceMembers" && named.Value.Value is bool b)
                return b;

        return false;
    }

    private static bool ReadAllowNonPublic(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "AllowNonPublic" && named.Value.Value is bool b)
                return b;

        return false;
    }

    /// <summary>
    ///     Reads <c>[DwarfMapper(NullCollections = ...)]</c>; defaults to <c>AsEmpty</c>.
    /// </summary>
    private static NullCollectionsBehavior ReadNullCollections(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "NullCollections" && named.Value.Value is int i)
                return (NullCollectionsBehavior)i;

        return NullCollectionsBehavior.AsEmpty;
    }

    /// <summary>
    ///     Reads <c>[DwarfMapper(ReferenceHandling = ...)]</c>; returns the integer value of the
    ///     <see cref="DwarfMapper.ReferenceHandlingStrategy" /> enum (0 = None, 1 = Preserve).
    ///     Defaults to 0 (None).
    /// </summary>
    private static int ReadReferenceHandling(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "ReferenceHandling" && named.Value.Value is int i)
                return i;

        return 0; // None
    }

    /// <summary>
    ///     Reads <c>[DwarfMapper(OnCycle = ...)]</c>; returns the integer value of the
    ///     <see cref="DwarfMapper.OnCycleStrategy" /> enum (0 = Throw, 1 = SetNull).
    ///     Defaults to 0 (Throw).
    /// </summary>
    private static int ReadOnCycle(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "OnCycle" && named.Value.Value is int i)
                return i;

        return 0; // Throw
    }

    /// <summary>Reads <c>[DwarfMapper(ImplicitConversions = ...)]</c>; defaults to <c>true</c> (permissive).</summary>
    private static bool ReadImplicitConversions(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "ImplicitConversions" && named.Value.Value is bool b)
                return b;
        return true;
    }

    /// <summary>
    ///     Reads <c>[DwarfMapper(RequiredMapping = ...)]</c>. Returns the enum's int value:
    ///     0 = <c>Target</c> (default — destination-coverage only), 1 = <c>Both</c> (also require every
    ///     source member consumed → DWARF039 for leftovers).
    /// </summary>
    private static int ReadRequiredMapping(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "RequiredMapping" && named.Value.Value is int v)
                return v;
        return 0; // RequiredMappingStrategy.Target
    }

    /// <summary>Reads <c>[DwarfMapper(NameConvention = ...)]</c>: 0 = Exact (default), 1 = Flexible.</summary>
    private static int ReadNameConvention(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        foreach (var named in attr.NamedArguments)
            if (named.Key == "NameConvention" && named.Value.Value is int v)
                return v;
        return 0; // NameConvention.Exact
    }

    /// <summary>
    ///     Canonical form for <see cref="NameConvention.Flexible" /> matching: removes <c>_</c> and lowercases,
    ///     so <c>PascalCase</c>/<c>camelCase</c>/<c>snake_case</c>/<c>UPPER_CASE</c> all reduce to the same key.
    /// </summary>
    private static string NormalizeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            if (c != '_')
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>
    ///     Adds the top-level source member consumed by <paramref name="sourceName" /> to
    ///     <paramref name="consumed" />. A dotted path (a flattened leaf like <c>"Address.City"</c>) marks its
    ///     root (<c>Address</c>) consumed; the empty sentinel (top-level collection / constant value) is ignored.
    /// </summary>
    private static void AddConsumed(HashSet<string> consumed, string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName))
            return;
        var dot = sourceName.IndexOf('.');
        consumed.Add(dot < 0 ? sourceName : sourceName.Substring(0, dot));
    }

    /// <summary>
    ///     Reads the per-method <c>[AutoNest(bool)]</c> attribute override, falling back to
    ///     <paramref name="classDefault" /> when the attribute is absent.
    /// </summary>
    private static bool ReadMethodAutoNest(IMethodSymbol method, bool classDefault)
    {
        foreach (var attr in method.GetAttributes())
            if (attr.AttributeClass?.ToDisplayString() == KnownNames.AutoNestFqn
                && attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is bool b)
                return b;

        return classDefault;
    }
}
