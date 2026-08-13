// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>
///     One <c>[DwarfSurfaceSite]</c> application, flattened out of the declaration.
/// </summary>
/// <param name="Site">The site(s) this claim replaces the element default for; may combine flags.</param>
/// <param name="AppliesTo">The endpoints claimed when the element is written at <paramref name="Site" />.</param>
/// <param name="Because">The structural reason, as stated at the declaration.</param>
internal sealed record SurfaceSiteClaim(AttributeTargets Site, SurfaceEndpoints AppliesTo, string Because);

/// <summary>One public surface element, as declared.</summary>
/// <param name="UsageName">The name as written in source, with "Attribute" and any generic arity stripped.</param>
/// <param name="AppliesTo">The element's DEFAULT claim; <paramref name="SiteClaims" /> refines it per site.</param>
/// <param name="SiteClaims">
///     The element's <c>[DwarfSurfaceSite]</c> narrowings. Being a collection, it compares by REFERENCE under
///     the record's generated equality, so two elements built from the same type in two separate
///     <see cref="SurfaceCatalog.Elements" /> constructions would not be equal. There is exactly one
///     construction — <see cref="SurfaceCatalog.Elements" /> is built once and every consumer, including
///     <c>CaseCache</c>, keys on those instances — so this changes nothing today. Whoever adds a second
///     construction path must key the caches on <see cref="SurfaceElement.Type" /> instead.
/// </param>
internal sealed record SurfaceElement(
    Type Type,
    string UsageName,
    SurfaceCategory Category,
    SurfaceEndpoints AppliesTo,
    string? ProbeKey,
    AttributeTargets ValidOn,
    bool AllowMultiple,
    IReadOnlyList<SurfaceSiteClaim> SiteClaims);

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
                    usage?.AllowMultiple ?? false,
                    SiteClaimsOf(x.Type));
            })
            .OrderBy(e => e.UsageName, StringComparer.Ordinal)
            .ThenBy(e => e.Type.GetGenericArguments().Length)
            .ToList();
    }

    /// <summary>Every <c>[DwarfSurfaceSite]</c> on a type, in declaration order.</summary>
    internal static IReadOnlyList<SurfaceSiteClaim> SiteClaimsOf(Type type) =>
        type.GetCustomAttributes<DwarfSurfaceSiteAttribute>(inherit: false)
            .Select(a => new SurfaceSiteClaim(a.Site, a.AppliesTo, a.Because))
            .ToList();

    /// <summary>
    ///     What this element claims to affect WHEN WRITTEN AT <paramref name="site" />: the site's own
    ///     <c>[DwarfSurfaceSite]</c> claim if it declares one, otherwise the element's default
    ///     <c>AppliesTo</c>.
    ///     <para>
    ///         Every cell's claim resolves through here, so the claim is always readable at the type it
    ///         describes. This replaced a per-(element, site, endpoint) predicate in
    ///         <c>SurfaceParityTests</c> that decided ~100 cells from the test project — nothing forced a
    ///         newly added element to acquire an entry there, and the cells it governed were reviewed by
    ///         no one.
    ///     </para>
    ///     <para>
    ///         <c>Single</c>, not <c>First</c>: two claims covering one site would hand the site to whichever
    ///         the reflection ordered first and leave the other silently inert. The overlap is separately
    ///         asserted by <c>SurfaceDeclarationTests</c>; this states the same invariant at the point of use
    ///         rather than trusting the gate to have run.
    ///     </para>
    /// </summary>
    public static SurfaceEndpoints ClaimFor(SurfaceElement element, AttributeTargets site)
    {
        ArgumentNullException.ThrowIfNull(element);
        var matches = element.SiteClaims.Where(sc => (sc.Site & site) == site).ToList();
        return matches.Count == 0 ? element.AppliesTo : matches.Single().AppliesTo;
    }

    /// <summary>
    ///     Everything wrong with an element's <c>[DwarfSurfaceSite]</c> declarations, as reader-facing lines.
    ///     <para>
    ///         A pure function over the declaration's own values rather than an inline loop in the gate, so
    ///         the malformed shapes can be fed to it directly. Every one of these checks passes vacuously
    ///         against today's two well-formed declarations; a gate that has never fired is unverified code,
    ///         and this is the same reason <see cref="SurfaceProbe.FirstNewOccurrence" /> is separated out.
    ///     </para>
    /// </summary>
    /// <param name="name">The element's usage name, for the message.</param>
    /// <param name="validOn">The element's <c>AttributeUsage.ValidOn</c>.</param>
    /// <param name="defaultAppliesTo">The element's <c>[DwarfSurface(AppliesTo = …)]</c>.</param>
    /// <param name="claims">Its <c>[DwarfSurfaceSite]</c> applications.</param>
    internal static IReadOnlyList<string> ValidateSiteClaims(string name, AttributeTargets validOn,
        SurfaceEndpoints defaultAppliesTo, IReadOnlyList<SurfaceSiteClaim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var problems = new List<string>();
        AttributeTargets seen = default; // the sites an earlier claim already covers

        foreach (var claim in claims)
        {
            if (claim.Site == 0)
                problems.Add($"{name}: a [DwarfSurfaceSite] names no site at all, so it narrows nothing and "
                             + "reads as a reviewed decision.");

            var illegal = claim.Site & ~validOn;
            if (illegal != 0)
                problems.Add($"{name}: [DwarfSurfaceSite({claim.Site})] names {illegal}, which "
                             + $"AttributeUsage does not permit (ValidOn = {validOn}). The element cannot be "
                             + "written there at all, so the narrowing applies to nothing — this is how a "
                             + "claim quietly stops applying after a site is removed from AttributeUsage.");

            var overlap = claim.Site & seen;
            if (overlap != 0)
                problems.Add($"{name}: two [DwarfSurfaceSite] claims both cover {overlap}. One of them would "
                             + "never be consulted, and which one depends on declaration order.");
            seen |= claim.Site;

            if (string.IsNullOrWhiteSpace(claim.Because))
                problems.Add($"{name}: [DwarfSurfaceSite({claim.Site})] states no reason. The reason must be "
                             + "about the SHAPE of the site; 'the generator does not read it there' is a "
                             + "divergence to report, not a claim to encode.");

            if (claim.AppliesTo == defaultAppliesTo)
                problems.Add($"{name}: [DwarfSurfaceSite({claim.Site})] restates the element's own default "
                             + $"({defaultAppliesTo}). It narrows nothing while reading as a reviewed "
                             + "decision — delete it, or narrow it.");
        }

        return problems;
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
