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

/// <summary>
///     One <c>[DwarfSurfaceProbe]</c> application, flattened out of the declaration.
/// </summary>
/// <param name="Property">The writable property whose cases this refines, or null in the constructor form.</param>
/// <param name="ConstructorArity">
///     The constructor overload's parameter count, or <see cref="DwarfSurfaceProbeAttribute.NotAConstructor" />
///     in the property form.
/// </param>
/// <param name="ProbeKey">The fixture this case is measured against, overriding the element's own.</param>
/// <param name="Value">Property form: the initialiser value, replacing the derived single-value domain.</param>
/// <param name="Arguments">Constructor form: the argument list, with <c>{Member}</c> placeholders.</param>
/// <param name="MapperOptions">The <c>[DwarfMapper(...)]</c> options this case needs to be reachable.</param>
/// <param name="Unmeasured">Why this case can pose no question at all, or null when it can.</param>
internal sealed record SurfaceProbeClaim(
    string? Property,
    int ConstructorArity,
    string? ProbeKey,
    string? Value,
    string? Arguments,
    string? MapperOptions,
    string? Unmeasured);

/// <summary>
///     One <c>[DwarfSurfaceOption]</c> application, flattened out of the declaration.
/// </summary>
/// <param name="Option">The writable property whose obligation this redirects.</param>
/// <param name="Category">The category whose obligation that option must satisfy instead.</param>
/// <param name="Because">Why its proof lives elsewhere, as stated at the declaration.</param>
internal sealed record SurfaceOptionClaim(string Option, SurfaceCategory Category, string Because);

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
/// <param name="ProbeClaims">
///     The element's <c>[DwarfSurfaceProbe]</c> refinements, one per case that needs its own shape, value or
///     argument list. Compares by reference for the same reason <paramref name="SiteClaims" /> does.
/// </param>
/// <param name="OptionClaims">
///     The element's <c>[DwarfSurfaceOption]</c> redirects, one per writable property whose proof obligation
///     is not the element's own. Compares by reference for the same reason <paramref name="SiteClaims" /> does.
/// </param>
internal sealed record SurfaceElement(
    Type Type,
    string UsageName,
    SurfaceCategory Category,
    SurfaceEndpoints AppliesTo,
    string? ProbeKey,
    AttributeTargets ValidOn,
    bool AllowMultiple,
    IReadOnlyList<SurfaceSiteClaim> SiteClaims,
    IReadOnlyList<SurfaceProbeClaim> ProbeClaims,
    IReadOnlyList<SurfaceOptionClaim> OptionClaims);

/// <summary>
///     One cell input: this element, written this way, at this declaration site.
/// </summary>
/// <param name="Rendered">The attribute exactly as it appears in source, brackets included.</param>
/// <param name="Axis">A short label naming what this case varies, for the failure message.</param>
/// <param name="ProbeKey">
///     The fixture THIS case is measured against — its own <c>[DwarfSurfaceProbe]</c> key if it declares one,
///     otherwise the element's. Per-case rather than per-element because an option bag asks one question per
///     property and one shape cannot answer eighteen of them.
/// </param>
/// <param name="Unmeasured">
///     Why this case can pose no question at all, or null when it can. A cell of such a case is excused on the
///     silent path and COUNTED, so the hole is declared rather than reported as a divergence.
/// </param>
/// <param name="MapperOptions">
///     The <c>[DwarfMapper(...)]</c> options the endpoint's mapper must carry for this case to be reachable at
///     all — the ambient conditions, stated at the declaration, under which the directive has anything to do.
/// </param>
internal sealed record SurfaceCase(
    SurfaceElement Element,
    AttributeTargets Site,
    string Rendered,
    string Axis,
    string? ProbeKey,
    string? Unmeasured,
    string? MapperOptions = null);

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
                    SiteClaimsOf(x.Type),
                    ProbeClaimsOf(x.Type),
                    OptionClaimsOf(x.Type));
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

    /// <summary>Every <c>[DwarfSurfaceProbe]</c> on a type, in declaration order.</summary>
    internal static IReadOnlyList<SurfaceProbeClaim> ProbeClaimsOf(Type type) =>
        type.GetCustomAttributes<DwarfSurfaceProbeAttribute>(inherit: false)
            .Select(a => new SurfaceProbeClaim(a.Property, a.ConstructorArity, a.ProbeKey, a.Value, a.Arguments,
                a.MapperOptions, a.Unmeasured))
            .ToList();

    /// <summary>Every <c>[DwarfSurfaceOption]</c> on a type, in declaration order.</summary>
    internal static IReadOnlyList<SurfaceOptionClaim> OptionClaimsOf(Type type) =>
        type.GetCustomAttributes<DwarfSurfaceOptionAttribute>(inherit: false)
            .Select(a => new SurfaceOptionClaim(a.Option, a.Category, a.Because))
            .ToList();

    /// <summary>
    ///     The category one writable property of an element must satisfy: its own
    ///     <c>[DwarfSurfaceOption]</c> redirect if it declares one, otherwise the ELEMENT's category.
    ///     <para>
    ///         Falling back to the element rather than to nothing is what makes the mechanism a redirect and
    ///         not an allowlist. An option nobody has thought about carries its element's obligation — for
    ///         <c>[DwarfMapper]</c> that is <c>ConsumerDirective</c>, i.e. "demonstrate it where a reader can
    ///         run it" — so the way to say less about an option is to state a different obligation, never to
    ///         say nothing.
    ///     </para>
    /// </summary>
    public static SurfaceCategory CategoryOfOption(SurfaceElement element, string option)
    {
        ArgumentNullException.ThrowIfNull(element);
        var matches = element.OptionClaims
            .Where(c => string.Equals(c.Option, option, StringComparison.Ordinal)).ToList();
        return matches.Count == 0 ? element.Category : matches.Single().Category;
    }

    /// <summary>
    ///     Everything wrong with an element's <c>[DwarfSurfaceOption]</c> redirects, as reader-facing lines.
    ///     A pure function over the declaration's own values, for the same reason
    ///     <see cref="ValidateSiteClaims" /> is one: every branch passes vacuously against the well-formed
    ///     declarations the repository actually has, and a gate that has never fired is unverified code.
    /// </summary>
    /// <param name="name">The element's usage name, for the message.</param>
    /// <param name="options">The element's writable property names.</param>
    /// <param name="elementCategory">The element's own <c>[DwarfSurface]</c> category.</param>
    /// <param name="claims">Its <c>[DwarfSurfaceOption]</c> applications.</param>
    internal static IReadOnlyList<string> ValidateOptionClaims(string name, IReadOnlySet<string> options,
        SurfaceCategory elementCategory, IReadOnlyList<SurfaceOptionClaim> claims)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(claims);

        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var claim in claims)
        {
            var where = $"[DwarfSurfaceOption(\"{claim.Option}\")]";

            if (!options.Contains(claim.Option))
                problems.Add($"{name}: {where} names no writable property of the element (it has: "
                             + $"{string.Join(", ", options.OrderBy(o => o, StringComparer.Ordinal))}), so it "
                             + "redirects nothing. The option it was written for still carries the element's "
                             + "obligation, and nothing says so.");
            else if (!seen.Add(claim.Option))
                problems.Add($"{name}: two [DwarfSurfaceOption] claims both redirect '{claim.Option}'. One is "
                             + "never consulted, and which one depends on declaration order.");

            if (string.IsNullOrWhiteSpace(claim.Because))
                problems.Add($"{name}: {where} states no reason. An unexplained redirect is an allowlist "
                             + "entry with a category name on it.");

            if (claim.Category == elementCategory)
                problems.Add($"{name}: {where} restates the element's own category ({elementCategory}). It "
                             + "redirects nothing while reading as a reviewed decision — delete it, or name "
                             + "the category whose obligation this option actually satisfies.");
        }

        return problems;
    }

    /// <summary>The refinement governing one writable property's cases, or null.</summary>
    private static SurfaceProbeClaim? ProbeFor(SurfaceElement element, string property) =>
        element.ProbeClaims.FirstOrDefault(
            p => string.Equals(p.Property, property, StringComparison.Ordinal));

    /// <summary>The refinement governing one constructor overload's case, or null.</summary>
    private static SurfaceProbeClaim? ProbeFor(SurfaceElement element, int arity) =>
        element.ProbeClaims.FirstOrDefault(p => p.Property is null && p.ConstructorArity == arity);

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

    /// <summary>
    ///     Everything wrong with an element's <c>[DwarfSurfaceProbe]</c> declarations, as reader-facing lines.
    ///     <para>
    ///         A pure function over the declaration's own values, for the same reason
    ///         <see cref="ValidateSiteClaims" /> is one: a refinement that governs no case is worse than a
    ///         missing one, because it reads as a reviewed decision while the cases it names are still probed
    ///         against a shape that cannot ask them anything. Every branch here passes vacuously against
    ///         today's well-formed declarations, so each is fed a malformed shape directly by
    ///         <c>SurfaceDeclarationTests</c>.
    ///     </para>
    /// </summary>
    /// <param name="name">The element's usage name, for the message.</param>
    /// <param name="domainSizes">Each writable property's name and the size of its DERIVED value domain.</param>
    /// <param name="arities">The parameter counts of the element's public constructors.</param>
    /// <param name="elementProbeKey">The element's own <c>[DwarfSurface(ProbeKey = ...)]</c>.</param>
    /// <param name="claims">Its <c>[DwarfSurfaceProbe]</c> applications.</param>
    /// <param name="fixtureNames">
    ///     Resolves the fixture a claim's key names to the member and type names it declares, or null when the
    ///     key binds to no fixture — an unbound key is the bijection gate's finding, not this one's, and
    ///     reporting it twice would name one defect in two places.
    /// </param>
    internal static IReadOnlyList<string> ValidateProbeClaims(string name,
        IReadOnlyDictionary<string, int> domainSizes, IReadOnlyList<int> arities, string? elementProbeKey,
        IReadOnlyList<SurfaceProbeClaim> claims,
        Func<string?, (IReadOnlySet<string> Members, IReadOnlySet<string> Types)?> fixtureNames)
    {
        ArgumentNullException.ThrowIfNull(domainSizes);
        ArgumentNullException.ThrowIfNull(arities);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(fixtureNames);

        var problems = new List<string>();
        var seenProperties = new HashSet<string>(StringComparer.Ordinal);
        var seenArities = new HashSet<int>();

        foreach (var claim in claims)
        {
            var where = claim.Property is not null
                ? $"[DwarfSurfaceProbe(\"{claim.Property}\")]"
                : $"[DwarfSurfaceProbe(constructorArity: {claim.ConstructorArity})]";

            if (claim.Property is not null)
            {
                if (!domainSizes.ContainsKey(claim.Property))
                    problems.Add($"{name}: {where} names no readable/writable non-indexed property of the "
                                 + "element, so it governs no case at all. Every cell it was written for is "
                                 + "still probed against a shape that cannot ask it anything.");
                else if (!seenProperties.Add(claim.Property))
                    problems.Add($"{name}: two [DwarfSurfaceProbe] claims both refine '{claim.Property}'. One "
                                 + "of them is never consulted, and which one depends on declaration order.");

                if (claim.Arguments is not null)
                    problems.Add($"{name}: {where} states Arguments, which only a CONSTRUCTOR case has. A "
                                 + "property case is rendered as a named argument, so this value goes nowhere.");

                if (claim.Value is not null && domainSizes.TryGetValue(claim.Property, out var size)
                                            && size != 1)
                    problems.Add($"{name}: {where} states a Value, but the derived domain of "
                                 + $"'{claim.Property}' has {size} members. Replacing a multi-member domain "
                                 + "with one value drops the others silently — and a domain whose second and "
                                 + "third members were never probed reads exactly like a covered one.");
            }
            else
            {
                if (!arities.Contains(claim.ConstructorArity))
                    problems.Add($"{name}: {where} names a constructor arity the element does not declare "
                                 + $"(it has {string.Join(", ", arities.OrderBy(a => a))}), so it governs no "
                                 + "case. This is how a refinement quietly stops applying after an overload "
                                 + "is added or removed.");
                else if (!seenArities.Add(claim.ConstructorArity))
                    problems.Add($"{name}: two [DwarfSurfaceProbe] claims both refine the "
                                 + $"{claim.ConstructorArity}-argument constructor. One is never consulted.");

                if (claim.Value is not null)
                    problems.Add($"{name}: {where} states a Value, which only a PROPERTY case has.");
            }

            if (claim.ProbeKey is null && claim.Value is null && claim.Arguments is null
                && claim.MapperOptions is null && claim.Unmeasured is null)
                problems.Add($"{name}: {where} states neither a ProbeKey, a Value, an argument list, mapper "
                             + "options nor an Unmeasured reason. It refines nothing while reading as a "
                             + "reviewed decision.");

            if (claim.ProbeKey is not null
                && string.Equals(claim.ProbeKey, elementProbeKey, StringComparison.Ordinal))
                problems.Add($"{name}: {where} restates the element's own ProbeKey ('{elementProbeKey}'). It "
                             + "refines nothing — delete it, or point it at the shape this case needs.");

            problems.AddRange(UnmeasuredProblems(name, where, claim, domainSizes.Count));
            problems.AddRange(ArgumentProblems(name, where, claim,
                fixtureNames(claim.ProbeKey ?? elementProbeKey)));
        }

        return problems;
    }

    /// <summary>
    ///     Why an <c>Unmeasured</c> declaration is not well-formed. Legal only on the zero-argument
    ///     constructor of an element that HAS writable properties — an option bag whose bare form selects
    ///     every default and therefore configures nothing.
    ///     <para>
    ///         That restriction is the whole safeguard. Excusing a cell is the same shape of act as narrowing
    ///         a claim, and the brief for this work is explicit that narrowing to keep a cell green converts a
    ///         live bug into documented intended behaviour. Confining the excuse to a case that renders with
    ///         no arguments at all makes it structurally impossible to mark a case that actually says
    ///         something as unmeasurable — the excuse cannot reach a cell where the generator had a decision
    ///         to make.
    ///     </para>
    /// </summary>
    private static IEnumerable<string> UnmeasuredProblems(string name, string where, SurfaceProbeClaim claim,
        int writablePropertyCount)
    {
        if (claim.Unmeasured is null) yield break;

        if (string.IsNullOrWhiteSpace(claim.Unmeasured))
            yield return $"{name}: {where} declares the case unmeasurable but states no reason.";

        if (claim.Property is not null || claim.ConstructorArity != 0)
            yield return $"{name}: {where} declares a case unmeasurable that is not the zero-argument "
                         + "constructor. Only a case that renders with NO arguments configures nothing by "
                         + "construction; anywhere else this would excuse a cell the generator actually had "
                         + "a decision to make, which is a divergence to report rather than a hole to declare.";

        if (writablePropertyCount == 0)
            yield return $"{name}: {where} declares the bare case unmeasurable, but the element has no "
                         + "writable properties — so the bare form IS the element, and excusing it excuses "
                         + "every question this element can pose.";

        if (claim.ProbeKey is not null || claim.Value is not null || claim.Arguments is not null
            || claim.MapperOptions is not null)
            yield return $"{name}: {where} is both unmeasurable and given a shape to measure it with. One of "
                         + "the two is wrong.";
    }

    /// <summary>
    ///     Why a declared argument list does not match the fixture it is used with: a <c>{Member}</c>
    ///     placeholder naming a member the fixture no longer declares, or a <c>typeof(X)</c> naming a type it
    ///     does not. Without this the argument silently degrades back into the no-op cell the declaration was
    ///     written to eliminate, and the resulting silence is indistinguishable from a real divergence.
    /// </summary>
    private static IEnumerable<string> ArgumentProblems(string name, string where, SurfaceProbeClaim claim,
        (IReadOnlySet<string> Members, IReadOnlySet<string> Types)? fixture)
    {
        if (claim.Arguments is null || fixture is not { } f) yield break;

        foreach (var member in MemberPlaceholders(claim.Arguments).Where(m => !f.Members.Contains(m)))
            yield return $"{name}: {where} names member '{member}', which the fixture it is measured against "
                         + $"does not declare (it has: {string.Join(", ", f.Members.OrderBy(x => x, StringComparer.Ordinal))}). "
                         + "The argument would render as a name matching nothing, the directive would apply "
                         + "to nothing, and the cell would read silent for a reason the generator had no "
                         + "part in.";

        foreach (var type in TypeReferences(claim.Arguments).Where(t => !f.Types.Contains(t)))
            yield return $"{name}: {where} names type '{type}', which the fixture it is measured against does "
                         + $"not declare (it has: {string.Join(", ", f.Types.OrderBy(x => x, StringComparer.Ordinal))}).";
    }

    /// <summary>Each writable property's name and the size of its DERIVED (un-refined) value domain.</summary>
    internal static IReadOnlyDictionary<string, int> DomainSizesOf(SurfaceElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return WritablePropertiesOf(element.Type)
            .ToDictionary(p => p.Name, p => ValueDomain(p, element.Type).Count(), StringComparer.Ordinal);
    }

    /// <summary>The parameter counts of an element's public constructors.</summary>
    internal static IReadOnlyList<int> ConstructorAritiesOf(SurfaceElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.Type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Select(c => c.GetParameters().Length).ToList();
    }

    /// <summary>
    ///     The member and type names of the fixture a probe key resolves to — the flat pair for a null key,
    ///     and <c>null</c> when the key names no fixture at all, which is the bijection gate's finding rather
    ///     than the argument gate's.
    /// </summary>
    internal static (IReadOnlySet<string> Members, IReadOnlySet<string> Types)? FixtureNames(string? probeKey)
    {
        if (probeKey is null) return SurfaceFixtures.DeclaredNames(EndpointSources.DefaultTypes);
        return SurfaceFixtures.Get(probeKey) is { } text ? SurfaceFixtures.DeclaredNames(text) : null;
    }

    /// <summary>The declaration sites this element is legal on, one flag at a time.</summary>
    public static IReadOnlyList<AttributeTargets> SitesOf(SurfaceElement element) =>
        Enum.GetValues<AttributeTargets>()
            .Where(t => t != AttributeTargets.All && int.PopCount((int)t) == 1)
            .Where(t => (element.ValidOn & t) == t)
            .ToList();

    /// <summary>The properties the property axis varies — public, readable, writable, non-indexed.</summary>
    internal static IReadOnlyList<PropertyInfo> WritablePropertiesOf(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanWrite: true, CanRead: true } && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

    private static List<SurfaceCase> BuildCases(SurfaceElement element)
    {
        var cases = new List<SurfaceCase>();
        var sites = SitesOf(element);
        var ctors = element.Type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        foreach (var site in sites)
        {
            // Axis 1 — each public constructor overload, with no properties set. A declared argument list
            // REPLACES the sampled one: which literal bites is irreducible knowledge (`false` for AutoNest,
            // `true` for the identically shaped MapNullSkip next door), and a sampled non-question reads in
            // the output exactly like a real silent divergence.
            foreach (var ctor in ctors)
            {
                var arity = ctor.GetParameters().Length;
                var claim = ProbeFor(element, arity);
                cases.Add(new SurfaceCase(element, site,
                    Render(element, ArgumentsFor(ctor, claim, variant: 1), ""), $"ctor({arity})",
                    claim?.ProbeKey ?? element.ProbeKey, claim?.Unmeasured, claim?.MapperOptions));
            }

            // Axis 2 — each writable property crossed with its FULL value domain, on the shortest ctor.
            var shortest = ctors.OrderBy(c => c.GetParameters().Length).FirstOrDefault();

            foreach (var p in WritablePropertiesOf(element.Type))
            {
                var claim = ProbeFor(element, p.Name);
                var baseArgs = shortest is null
                    ? ""
                    : ArgumentsFor(shortest, ProbeFor(element, shortest.GetParameters().Length), variant: 1);

                foreach (var value in ValueDomainFor(element, p, claim))
                    cases.Add(new SurfaceCase(element, site,
                        Render(element, baseArgs, $"{p.Name} = {value}"), $"{p.Name}={value}",
                        claim?.ProbeKey ?? element.ProbeKey, claim?.Unmeasured, claim?.MapperOptions));
            }

            // Axis 3 — multiplicity, where the attribute permits it. With a DECLARED argument list the two
            // applications are identical, and deliberately so: "the same real directive stated twice" is the
            // sharpest form of the multiplicity question, whereas inventing a second sampled argument would
            // reintroduce the non-question the declaration exists to remove.
            if (element.AllowMultiple && ctors.Length > 0)
            {
                var claim = ProbeFor(element, ctors[0].GetParameters().Length);
                var one = Render(element, ArgumentsFor(ctors[0], claim, variant: 1), "");
                var two = Render(element, ArgumentsFor(ctors[0], claim, variant: 2), "");
                cases.Add(new SurfaceCase(element, site, one + "\n" + two, "×2",
                    claim?.ProbeKey ?? element.ProbeKey, claim?.Unmeasured, claim?.MapperOptions));
            }
        }

        return cases;
    }

    /// <summary>
    ///     The argument list for one constructor case: the declaration's, with its <c>{Member}</c>
    ///     placeholders expanded, or the sampled one.
    ///     <para>
    ///         The sampled path passes <paramref name="variant" /> through a lambda rather than a method
    ///         group. <c>Select(SampleArgument)</c> binds the INDEXED overload of <c>Select</c>, so the
    ///         parameter's position was silently passed as the variant — parameter 0 of every constructor was
    ///         rendered with variant 0, and the multiplicity axis (variant 2) rendered the same literal again,
    ///         emitting two byte-identical applications where the axis exists to vary them.
    ///     </para>
    /// </summary>
    private static string ArgumentsFor(ConstructorInfo ctor, SurfaceProbeClaim? claim, int variant) =>
        claim?.Arguments is { } declared
            ? ExpandArguments(declared)
            : string.Join(", ", ctor.GetParameters()
                .Select((p, position) => SampleArgument(p, variant, position)));

    /// <summary>
    ///     Expands <c>{Member}</c> to a quoted member-name literal. Everything else is passed through
    ///     verbatim, so a literal that deliberately is NOT a member — a constant value, a converter name —
    ///     stays visibly unchecked. The names themselves are validated against the fixture by
    ///     <see cref="ValidateProbeClaims" />; expansion is text, checking is the gate's job.
    /// </summary>
    internal static string ExpandArguments(string declared)
    {
        ArgumentNullException.ThrowIfNull(declared);
        return System.Text.RegularExpressions.Regex.Replace(declared, @"\{(\w+)\}", m => $"\"{m.Groups[1].Value}\"");
    }

    /// <summary>Every <c>{Member}</c> placeholder in a declared argument list.</summary>
    internal static IReadOnlyList<string> MemberPlaceholders(string declared) =>
        System.Text.RegularExpressions.Regex.Matches(declared ?? "", @"\{(\w+)\}")
            .Select(m => m.Groups[1].Value).ToList();

    /// <summary>Every <c>typeof(X)</c> the declared argument list names.</summary>
    internal static IReadOnlyList<string> TypeReferences(string declared) =>
        System.Text.RegularExpressions.Regex.Matches(declared ?? "", @"typeof\(\s*(\w+)\s*\)")
            .Select(m => m.Groups[1].Value).ToList();

    /// <summary>
    ///     The value domain for one property case: the declaration's single stated value where it has one,
    ///     otherwise the derived full domain. Stating a value is only legal where the derived domain holds a
    ///     single member (see <see cref="ValidateProbeClaims" />), so this can never quietly shrink an enum's
    ///     domain to one member and report it as covered.
    /// </summary>
    private static IEnumerable<string> ValueDomainFor(SurfaceElement element, PropertyInfo p,
        SurfaceProbeClaim? claim) =>
        claim?.Value is { } stated ? [stated] : ValueDomain(p, element.Type);

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

    /// <summary>
    ///     A compilable literal for a constructor parameter, varying by both <paramref name="variant" /> and
    ///     the parameter's <paramref name="position" />.
    ///     <para>
    ///         Both dimensions are load-bearing and each was broken on its own axis. Position must vary so a
    ///         two-string constructor does not render <c>("Name", "Name")</c> — an identity binding that
    ///         auto-matching already produces, whose silence cannot be told from a discarded directive.
    ///         Variant must vary so the multiplicity axis renders two DIFFERENT applications; it did not,
    ///         because <c>Select(SampleArgument)</c> bound the INDEXED overload of <c>Select</c> and passed
    ///         the position where the variant was expected, so a one-argument constructor emitted the same
    ///         literal twice and the axis asked nothing it had not already asked.
    ///     </para>
    ///     <para>
    ///         The two are combined into one rotation over the flat pair's member names, which keeps every
    ///         variant-1 rendering byte-identical to what the matrix produced before the fix — the churn is
    ///         confined to the second application of the multiplicity axis, where it belongs.
    ///     </para>
    /// </summary>
    private static string SampleArgument(ParameterInfo p, int variant, int position)
    {
        var t = p.ParameterType;
        if (t == typeof(string))
        {
            string[] names = ["Id", "Name"];
            return $"\"{names[(position + variant - 1) % names.Length]}\"";
        }

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
