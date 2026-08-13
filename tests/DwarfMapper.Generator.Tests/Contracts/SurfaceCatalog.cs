// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>One public surface element, as declared.</summary>
/// <param name="UsageName">The name as written in source, with "Attribute" and any generic arity stripped.</param>
internal sealed record SurfaceElement(
    Type Type,
    string UsageName,
    SurfaceCategory Category,
    SurfaceEndpoints AppliesTo,
    string? ProbeKey,
    AttributeTargets ValidOn,
    bool AllowMultiple);

/// <summary>
///     One cell input: this element, written this way, at this declaration site.
/// </summary>
/// <param name="Rendered">The attribute exactly as it appears in source, brackets included.</param>
/// <param name="Axis">A short label naming what this case varies, for the failure message.</param>
internal sealed record SurfaceCase(SurfaceElement Element, AttributeTargets Site, string Rendered, string Axis);

/// <summary>
///     The shipped surface and its case-space, DERIVED rather than listed.
///     <para>
///         An earlier arrangement kept the option list by hand and caught omissions with a growth ratchet,
///         which is not the same as not having the problem. Everything derivable is derived here: the element
///         set is the assembly's <c>[DwarfSurface]</c>-marked types; the declaration sites are
///         <c>AttributeUsage.ValidOn</c> decomposed; the constructor cases are the public constructors; the
///         property cases are each writable property crossed with its full value domain. Add attribute 33 and
///         it appears in the matrix with no list to remember to update.
///     </para>
/// </summary>
internal static class SurfaceCatalog
{
    public static IReadOnlyList<SurfaceElement> Elements { get; } = Build();

    private static readonly Dictionary<SurfaceElement, IReadOnlyList<SurfaceCase>> CaseCache = new();

    public static IReadOnlyList<SurfaceCase> CasesFor(SurfaceElement element)
    {
        lock (CaseCache)
        {
            if (CaseCache.TryGetValue(element, out var cached)) return cached;
            var built = BuildCases(element);
            CaseCache[element] = built;
            return built;
        }
    }

    /// <summary>Every element whose category demands the executed cross-product.</summary>
    public static IReadOnlyList<SurfaceElement> CrossProductElements { get; } =
        Elements.Where(e => e.Category is SurfaceCategory.ConsumerDirective or SurfaceCategory.EmissionShape)
            .ToList();

    private static List<SurfaceElement> Build()
    {
        return typeof(DwarfMapperAttribute).Assembly.GetExportedTypes()
            .Where(t => t is { IsAbstract: false } && typeof(Attribute).IsAssignableFrom(t))
            .Select(t => (Type: t, Surface: t.GetCustomAttribute<DwarfSurfaceAttribute>(inherit: false)))
            .Where(x => x.Surface is not null)
            .Select(x =>
            {
                var usage = x.Type.GetCustomAttribute<AttributeUsageAttribute>(inherit: true);
                return new SurfaceElement(
                    x.Type,
                    UsageName(x.Type.Name),
                    x.Surface!.Category,
                    x.Surface.AppliesTo,
                    x.Surface.ProbeKey,
                    usage?.ValidOn ?? AttributeTargets.All,
                    usage?.AllowMultiple ?? false);
            })
            .OrderBy(e => e.UsageName, StringComparer.Ordinal)
            .ThenBy(e => e.Type.GetGenericArguments().Length)
            .ToList();
    }

    /// <summary>The declaration sites this element is legal on, one flag at a time.</summary>
    public static IReadOnlyList<AttributeTargets> SitesOf(SurfaceElement element) =>
        Enum.GetValues<AttributeTargets>()
            .Where(t => t != AttributeTargets.All && int.PopCount((int)t) == 1)
            .Where(t => (element.ValidOn & t) == t)
            .ToList();

    private static List<SurfaceCase> BuildCases(SurfaceElement element)
    {
        var cases = new List<SurfaceCase>();
        var sites = SitesOf(element);
        var ctors = element.Type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        foreach (var site in sites)
        {
            // Axis 1 — each public constructor overload, with no properties set.
            foreach (var ctor in ctors)
            {
                var args = string.Join(", ", ctor.GetParameters().Select(SampleArgument));
                var rendered = Render(element, args, "");
                cases.Add(new SurfaceCase(element, site, rendered, $"ctor({ctor.GetParameters().Length})"));
            }

            // Axis 2 — each writable property crossed with its FULL value domain, on the shortest ctor.
            var shortest = ctors.OrderBy(c => c.GetParameters().Length).FirstOrDefault();
            var baseArgs = shortest is null
                ? ""
                : string.Join(", ", shortest.GetParameters().Select(SampleArgument));

            foreach (var p in element.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p is { CanWrite: true, CanRead: true }
                                     && p.GetIndexParameters().Length == 0)
                         .OrderBy(p => p.Name, StringComparer.Ordinal))
            foreach (var value in ValueDomain(p, element.Type))
            {
                var rendered = Render(element, baseArgs, $"{p.Name} = {value}");
                cases.Add(new SurfaceCase(element, site, rendered, $"{p.Name}={value}"));
            }

            // Axis 3 — multiplicity, where the attribute permits it.
            if (element.AllowMultiple && ctors.Length > 0)
            {
                var one = Render(element, string.Join(", ",
                    ctors[0].GetParameters().Select(SampleArgument)), "");
                var two = Render(element, string.Join(", ",
                    ctors[0].GetParameters().Select(p => SampleArgument(p, variant: 2))), "");
                cases.Add(new SurfaceCase(element, site, one + "\n" + two, "×2"));
            }
        }

        return cases;
    }

    /// <summary>
    ///     Every value a property can take that differs from its default — the FULL domain, not the first
    ///     alternative. A three-member enum whose second and third members were never probed reads exactly
    ///     like one that was fully covered.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Reflective construction of an arbitrary attribute type (possibly open-generic, "
        + "possibly with a throwing constructor) can fail in ways this catalogue does not control; the domain "
        + "is still the type's full value set regardless, so any failure here just means the default is "
        + "unknown, not that the property should silently drop out of the matrix.")]
    private static IEnumerable<string> ValueDomain(PropertyInfo p, Type declaring)
    {
        object? def = null;
        try
        {
            var ctor = declaring.GetConstructors().OrderBy(c => c.GetParameters().Length).First();
            var instance = ctor.Invoke(ctor.GetParameters().Select(SampleValue).ToArray());
            def = p.GetValue(instance);
        }
        catch (Exception)
        {
            // Some attributes cannot be constructed reflectively (open generics). The domain is still the
            // type's full value set; the default is simply unknown, so every value is emitted.
        }

        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;

        if (t == typeof(bool))
        {
            // Compares against the rendered lowercase literal directly rather than case-converting the
            // default's ToString(), so there is no case-folding call for CA1308 to flag as a normalization
            // hazard — this is a literal match, not a security-sensitive comparison.
            var defRendered = def switch { true => "true", false => "false", _ => null };
            return new[] { "true", "false" }.Where(v => defRendered is null || v != defRendered);
        }

        if (t.IsEnum)
            return Enum.GetValues(t).Cast<object>()
                .Where(v => def is null || !v.Equals(def))
                .Select(v => $"{t.Name}.{v}");

        if (t == typeof(int))
            return [def is int i ? (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "1"];

        if (t == typeof(string))
            return ["\"probe\""];

        if (t == typeof(Type))
            return ["typeof(Dst)"];

        // object-typed attribute properties (e.g. MapPropertyAttribute.NullSubstitute) accept any of the
        // attribute-parameter constant types; a string literal is a compilable, unambiguous representative.
        if (t == typeof(object))
            return ["\"probe\""];

        // string[]-typed attribute properties (e.g. RestatesBaseAttribute.Overrides) take an inline array
        // initializer. Arrays have no value equality, so — like string and Type above — this always emits one
        // non-default representative rather than trying to diff against the (always-empty) default.
        if (t == typeof(string[]))
            return ["new[] { \"Name\" }"];

        throw new InvalidOperationException(
            $"No value domain for {declaring.Name}.{p.Name} of type {t.Name}. Add one rather than letting the "
            + "property silently fall out of the matrix — an unprobed property is exactly the hole this "
            + "catalogue exists to close.");
    }

    /// <summary>A compilable literal for a constructor parameter.</summary>
    private static string SampleArgument(ParameterInfo p, int variant = 1)
    {
        var t = p.ParameterType;
        if (t == typeof(string)) return variant == 1 ? "\"Name\"" : "\"Id\"";
        if (t == typeof(Type)) return "typeof(Dst)";
        if (t == typeof(bool)) return "true";
        if (t == typeof(int)) return variant.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (t.IsEnum) return $"{t.Name}.{Enum.GetNames(t)[0]}";

        // object-typed parameters (e.g. MapValueAttribute.value) accept any attribute-parameter constant; a
        // string literal is compilable and unambiguous.
        if (t == typeof(object)) return "\"probe\"";

        // params Type[] (MapToAttribute.targets): a single argument satisfies the params array, and the two
        // variants differ so the ×2-multiplicity axis renders two distinct, still-compilable applications.
        if (t.IsArray && t.GetElementType() == typeof(Type)) return variant == 1 ? "typeof(Dst)" : "typeof(Src)";

        throw new InvalidOperationException(
            $"No sample argument for parameter '{p.Name}' of type {t.Name}. Add one; skipping it would drop "
            + "the constructor overload from the matrix without saying so.");
    }

    private static object? SampleValue(ParameterInfo p)
    {
        var t = p.ParameterType;
        if (t == typeof(string)) return "Name";
        if (t == typeof(Type)) return typeof(object);
        if (t == typeof(bool)) return true;
        if (t == typeof(int)) return 1;
        if (t.IsEnum) return Enum.GetValues(t).GetValue(0);
        throw new InvalidOperationException($"No sample value for {p.ParameterType.Name}.");
    }

    /// <summary>Writes the attribute as it appears in source, including any generic type arguments.</summary>
    private static string Render(SurfaceElement element, string ctorArgs, string namedArgs)
    {
        var generics = element.Type.GetGenericArguments().Length switch
        {
            0 => "",
            1 => "<Dst>",
            2 => "<Src, Dst>",
            _ => throw new InvalidOperationException(
                $"{element.UsageName} has arity {element.Type.GetGenericArguments().Length}; add a rendering.")
        };

        var args = string.Join(", ", new[] { ctorArgs, namedArgs }.Where(s => !string.IsNullOrEmpty(s)));
        return args.Length == 0
            ? $"[{element.UsageName}{generics}]"
            : $"[{element.UsageName}{generics}({args})]";
    }

    private static string UsageName(string typeName)
    {
        var tick = typeName.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0) typeName = typeName[..tick];
        return typeName.EndsWith("Attribute", StringComparison.Ordinal)
            ? typeName[..^"Attribute".Length]
            : typeName;
    }
}
