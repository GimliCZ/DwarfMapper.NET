// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     The forcing function for the whole surface-coverage architecture: a public attribute that carries no
    ///     <c>[DwarfSurface]</c> has no category, therefore no obligation, therefore no proof. Before this gate,
    ///     thirteen public attributes had zero consumer-shaped presence and nothing in the repository failed.
    /// </summary>
    public sealed class SurfaceDeclarationTests
    {
        /// <summary>
        ///     The element count the executed cross-product is pinned to. See
        ///     <see cref="The_executed_cross_product_covers_exactly_the_elements_it_is_pinned_to" /> for why this
        ///     is a pin and not a bound.
        /// </summary>
        // 29 -> 30 (round 29 T3.1): [MapShare]. Re-measured in the commit that added it, as the pin requires.
        private const int CrossProductElementCount = 30;

        /// <summary>Every public attribute type shipped by the runtime package.</summary>
        public static IReadOnlyList<Type> PublicAttributeTypes { get; } =
            typeof(DwarfMapperAttribute).Assembly.GetExportedTypes()
                .Where(t => t is { IsAbstract: false } && typeof(Attribute).IsAssignableFrom(t))
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .ToList();

        [Fact]
        public void The_public_attribute_scan_is_not_vacuous()
        {
            Assert.True(PublicAttributeTypes.Count >= 30,
                $"Only {PublicAttributeTypes.Count} public attribute types reflected; the surface has ~32. " + "Either the package genuinely shrank (lower this floor deliberately) or the reflection stopped " + "seeing the surface and every gate built on it has gone vacuous.");
        }

        [Fact]
        public void Every_public_attribute_declares_a_DwarfSurface_category()
        {
            var undeclared = PublicAttributeTypes
                .Where(t => t.GetCustomAttribute<DwarfSurfaceAttribute>(false) is null)
                .Select(t => t.Name)
                .ToList();

            Assert.True(undeclared.Count == 0,
                "Public attribute type(s) with no [DwarfSurface] declaration:\n  " +
                string.Join("\n  ", undeclared) +
                "\n\nAdd [DwarfSurface(SurfaceCategory.X, ...)] to the declaration. There is deliberately no " +
                "'exempt' category: every category carries a proof obligation, and choosing one is how the " +
                "obligation gets assigned. See docs/superpowers/specs/" +
                "2026-08-13-surface-coverage-architecture-design.md for the category table.");
        }

        [Fact]
        public void SurfaceEndpoints_and_Endpoint_name_the_same_seven_endpoints()
        {
            // Two enums that drift apart would silently repoint every AppliesTo claim at the wrong endpoint,
            // and the matrix would keep passing while measuring the wrong cell.
            var flags = Enum.GetNames<SurfaceEndpoints>()
                .Where(n => n is not ("None" or "All"))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            var endpoints = Enum.GetNames<Endpoint>()
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(endpoints, flags);
        }

        [Fact]
        public void Every_catalog_element_yields_at_least_one_case()
        {
            var barren = SurfaceCatalog.Elements
                .Where(e => SurfaceCatalog.CasesFor(e).Count == 0)
                .Select(e => e.UsageName)
                .ToList();

            Assert.True(barren.Count == 0,
                "Surface element(s) that produce NO probe case, so the matrix silently skips them entirely:\n  " + string.Join("\n  ", barren) + "\n\nThis is the vacuity failure the whole arrangement exists to prevent: an element with no " + "cases passes every cell it has, which is none.");
        }

        [Fact]
        public void The_catalog_produces_a_case_count_in_the_expected_order_of_magnitude()
        {
            var total = SurfaceCatalog.Elements.Sum(e => SurfaceCatalog.CasesFor(e).Count);
            Assert.InRange(total, 60, 4000);
        }

        /// <summary>The number of elements the executed cross-product actually covers, pinned exactly.</summary>
        /// <remarks>
        ///     <para>
        ///         <c>CrossProductElements</c> filters the catalogue to <c>ConsumerDirective</c> and
        ///         <c>EmissionShape</c>, and it is that filtered list — not <c>Elements</c> — that the surface
        ///         matrix enumerates. Nothing counted its size, so re-categorising one attribute deleted its
        ///         whole seven-endpoints-by-cases block from the executed matrix, replaced it with whatever
        ///         weaker check its new category attracts, and moved no number anywhere. That is the accounting
        ///         gap B32 records, one level up: a population can leave the matrix silently.
        ///     </para>
        ///     <para>
        ///         <see cref="The_catalog_produces_a_case_count_in_the_expected_order_of_magnitude" /> does NOT
        ///         cover this. It sums cases over <c>Elements</c>, the unfiltered set, so an element that moves
        ///         between categories keeps contributing to its total and the range is two orders of magnitude
        ///         wide besides.
        ///     </para>
        ///     <para>
        ///         Pinned rather than bounded: a change here means an element moved into or out of the executed
        ///         cross-product, which is a deliberate act — adding a directive, retiring one, or re-reading
        ///         what an attribute IS. Restate the number in the same commit that makes the move, and say
        ///         which element moved and why.
        ///     </para>
        /// </remarks>
        [Fact]
        public void The_executed_cross_product_covers_exactly_the_elements_it_is_pinned_to()
        {
            var covered = SurfaceCatalog.CrossProductElements
                .Select(e => e.UsageName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.True(covered.Count == CrossProductElementCount,
                $"The executed cross-product covers {covered.Count} elements, not the pinned " +
                $"{CrossProductElementCount}:\n  " +
                string.Join("\n  ", covered) +
                "\n\nAn element moved into or out of the executed matrix. Only ConsumerDirective and " +
                "EmissionShape elements are measured cell by cell across all seven endpoints; every other " +
                "category is judged by something weaker. Confirm the move was intended, name it in the commit " +
                "message, and restate this number there.");
        }

        /// <summary>
        ///     Every <c>[DwarfSurfaceSite]</c> narrowing is well-formed: it names only sites the element's
        ///     <c>AttributeUsage</c> permits, no two claims cover one site, it states a reason, and it actually
        ///     narrows something.
        ///     <para>
        ///         A malformed override is worse than a missing one. It reads as a reviewed, structural decision
        ///         while the cells it was meant to govern are decided by the element default — a stale site (one
        ///         removed from <c>AttributeUsage</c> later) is exactly how a claim quietly stops applying, and a
        ///         second claim on the same site hands the site to whichever the reflection ordered first.
        ///     </para>
        /// </summary>
        [Fact]
        public void Every_declared_site_override_is_well_formed()
        {
            var problems = SurfaceCatalog.Elements
                .SelectMany(e => SurfaceCatalog.ValidateSiteClaims(
                    e.UsageName,
                    e.ValidOn,
                    e.AppliesTo,
                    e.SiteClaims))
                .ToList();

            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        /// <summary>
        ///     Feeds <c>ValidateSiteClaims</c> each malformed shape directly. Against the two well-formed
        ///     declarations the repository actually has, every branch of that validator passes vacuously, and a
        ///     gate that has never fired is unverified code — the same reason
        ///     <c>SurfaceProbe.FirstNewOccurrence</c> is a separated pure function with its own test.
        /// </summary>
        [Fact]
        public void The_site_override_validator_rejects_every_malformed_shape()
        {
            const AttributeTargets validOn = AttributeTargets.Method | AttributeTargets.Property;
            const SurfaceEndpoints def = SurfaceEndpoints.All;

            static string[] Check(AttributeTargets on, SurfaceEndpoints fallback, params SurfaceSiteClaim[] cs)
            {
                return SurfaceCatalog.ValidateSiteClaims("Probe", on, fallback, cs).ToArray();
            }

            // A site AttributeUsage does not permit: the narrowing applies to nothing.
            Assert.Contains("Field",
                Assert.Single(Check(validOn,
                    def,
                    new SurfaceSiteClaim(AttributeTargets.Field, SurfaceEndpoints.Registry, "shape"))),
                StringComparison.Ordinal);

            // Partially illegal: Property is fine, Field is not, and the legal half must not excuse the other.
            Assert.Contains("Field",
                Assert.Single(Check(validOn,
                    def,
                    new SurfaceSiteClaim(AttributeTargets.Property | AttributeTargets.Field,
                        SurfaceEndpoints.Registry,
                        "shape"))),
                StringComparison.Ordinal);

            // Two claims covering one site.
            Assert.Contains("both cover",
                Assert.Single(Check(validOn,
                    def,
                    new SurfaceSiteClaim(AttributeTargets.Property, SurfaceEndpoints.Registry, "shape"),
                    new SurfaceSiteClaim(AttributeTargets.Property, SurfaceEndpoints.CreateMap, "shape"))),
                StringComparison.Ordinal);

            // Overlapping rather than identical: Property is claimed twice, Method only once.
            Assert.Contains("both cover",
                Assert.Single(Check(validOn,
                    def,
                    new SurfaceSiteClaim(AttributeTargets.Property, SurfaceEndpoints.Registry, "shape"),
                    new SurfaceSiteClaim(AttributeTargets.Property | AttributeTargets.Method,
                        SurfaceEndpoints.CreateMap,
                        "shape"))),
                StringComparison.Ordinal);

            // No reason stated.
            Assert.Contains("states no reason",
                Assert.Single(Check(validOn,
                    def,
                    new SurfaceSiteClaim(AttributeTargets.Property, SurfaceEndpoints.Registry, "   "))),
                StringComparison.Ordinal);

            // Restates the default: narrows nothing.
            Assert.Contains("restates",
                Assert.Single(Check(validOn,
                    def,
                    new SurfaceSiteClaim(AttributeTargets.Property, def, "shape"))),
                StringComparison.Ordinal);

            // Names no site at all.
            Assert.Contains("no site at all",
                Assert.Single(Check(validOn,
                    def,
                    new SurfaceSiteClaim(default, SurfaceEndpoints.Registry, "shape"))),
                StringComparison.Ordinal);

            // The well-formed shape the repository actually uses reports nothing.
            Assert.Empty(Check(validOn,
                def,
                new SurfaceSiteClaim(AttributeTargets.Property, SurfaceEndpoints.Registry, "shape")));
        }

        /// <summary>
        ///     Every <c>[DwarfSurfaceOption]</c> redirect is well-formed: it names a writable property the element
        ///     actually has, no two redirect the same one, it states a reason, and it redirects somewhere other
        ///     than the element's own category.
        ///     <para>
        ///         A redirect that governs nothing is the failure mode this mechanism was built to replace. The
        ///         option it names silently falls back to the element's obligation while the declaration reads as
        ///         a reviewed decision about where that option's proof lives — which is a <c>NotDemonstrable</c>
        ///         entry again, in better handwriting.
        ///     </para>
        /// </summary>
        [Fact]
        public void Every_declared_option_redirect_is_well_formed()
        {
            var problems = SurfaceCatalog.Elements
                .SelectMany(e => SurfaceCatalog.ValidateOptionClaims(
                    e.UsageName,
                    SurfaceCatalog.WritablePropertiesOf(e.Type)
                        .Select(p => p.Name).ToHashSet(StringComparer.Ordinal),
                    e.Category,
                    e.OptionClaims))
                .ToList();

            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        /// <summary>
        ///     Feeds <c>ValidateOptionClaims</c> each malformed shape directly, for the same reason
        ///     <see cref="The_site_override_validator_rejects_every_malformed_shape" /> does: against the two
        ///     well-formed redirects the repository has, every branch of that validator passes vacuously.
        /// </summary>
        [Fact]
        public void The_option_redirect_validator_rejects_every_malformed_shape()
        {
            var options = new HashSet<string>(StringComparer.Ordinal)
            {
                "Flag",
                "Mode"
            };

            static string[] Check(IReadOnlySet<string> known, params SurfaceOptionClaim[] cs)
            {
                return SurfaceCatalog.ValidateOptionClaims("Probe",
                    known,
                    SurfaceCategory.ConsumerDirective,
                    cs).ToArray();
            }

            // Names a property the element does not have: the redirect governs nothing.
            Assert.Contains("no writable property",
                Assert.Single(Check(options,
                    new SurfaceOptionClaim("Gone", SurfaceCategory.EmissionShape, "why"))),
                StringComparison.Ordinal);

            // Two redirects on one option: one is never consulted.
            Assert.Contains("both redirect",
                Assert.Single(Check(options,
                    new SurfaceOptionClaim("Flag", SurfaceCategory.EmissionShape, "why"),
                    new SurfaceOptionClaim("Flag", SurfaceCategory.BuildFailureOnly, "why"))),
                StringComparison.Ordinal);

            // No reason stated.
            Assert.Contains("states no reason",
                Assert.Single(Check(options,
                    new SurfaceOptionClaim("Flag", SurfaceCategory.EmissionShape, "  "))),
                StringComparison.Ordinal);

            // Restates the element's own category: redirects nothing.
            Assert.Contains("restates",
                Assert.Single(Check(options,
                    new SurfaceOptionClaim("Flag", SurfaceCategory.ConsumerDirective, "why"))),
                StringComparison.Ordinal);

            // The well-formed shape the repository actually uses reports nothing.
            Assert.Empty(Check(options, new SurfaceOptionClaim("Flag", SurfaceCategory.EmissionShape, "why")));
        }

        /// <summary>
        ///     A <c>[DwarfSurfaceOption]</c> on a type with no <c>[DwarfSurface]</c> redirects an obligation the
        ///     type does not have, and the type is not in the catalogue at all — so the redirect is never read.
        /// </summary>
        [Fact]
        public void No_option_redirect_sits_on_a_type_that_declares_no_category()
        {
            var orphans = typeof(DwarfMapperAttribute).Assembly.GetExportedTypes()
                .Where(t => t.GetCustomAttributes<DwarfSurfaceOptionAttribute>(false).Any() && t.GetCustomAttribute<DwarfSurfaceAttribute>(false) is null)
                .Select(t => t.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.True(orphans.Count == 0,
                "Type(s) carrying [DwarfSurfaceOption] but no [DwarfSurface]: " + string.Join(", ", orphans) + ". The redirect names an obligation for a type that has none.");
        }

        /// <summary>
        ///     A <c>[DwarfSurfaceSite]</c> on a type with no <c>[DwarfSurface]</c> has no default to narrow, and
        ///     <c>SurfaceCatalog</c> filters the type out of the element set entirely — so the override, and every
        ///     cell it was written to govern, would vanish without a word.
        /// </summary>
        [Fact]
        public void No_site_override_sits_on_a_type_that_declares_no_category()
        {
            var orphans = typeof(DwarfMapperAttribute).Assembly.GetExportedTypes()
                .Where(t => t.GetCustomAttributes<DwarfSurfaceSiteAttribute>(false).Any() && t.GetCustomAttribute<DwarfSurfaceAttribute>(false) is null)
                .Select(t => t.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.True(orphans.Count == 0,
                "Type(s) carrying [DwarfSurfaceSite] but no [DwarfSurface]: " + string.Join(", ", orphans) + ". The override narrows a default that does not exist, and the type is not in the catalogue at " + "all, so neither the claim nor its cells are ever looked at.");
        }

        /// <summary>
        ///     The site overrides are load-bearing, not decoration: at least one element must resolve DIFFERENT
        ///     claims at two of its own sites. If every override were deleted — or <c>ClaimFor</c> stopped
        ///     consulting them — this fails, rather than the matrix quietly going back to one claim per element
        ///     and ~100 cells changing verdict with nothing to say so.
        /// </summary>
        [Fact]
        public void At_least_one_element_claims_differently_at_two_of_its_sites()
        {
            var siteAware = SurfaceCatalog.CrossProductElements
                .Where(e => SurfaceCatalog.SitesOf(e)
                                .Select(s => SurfaceCatalog.ClaimFor(e, s))
                                .Distinct()
                                .Count() >
                            1)
                .Select(e => e.UsageName)
                .ToList();

            Assert.True(siteAware.Count >= 2,
                $"Only {siteAware.Count} element(s) resolve a different claim at different sites " +
                $"({string.Join(", ", siteAware)}). Two do: MapProperty and MapIgnore, whose member placement " +
                "is the [MapTo] registry form rather than a second way to configure a mapper. Fewer means " +
                "either an override was deleted or ClaimFor stopped reading them, and ~100 member-site cells " +
                "are back to being judged by the element-wide default.");
        }

        /// <summary>
        ///     There are two demand sources, not one. Element-level: <c>[DwarfSurface(ProbeKey = "…")]</c> on an
        ///     attribute TYPE, the shape its whole case-space is measured against. Case-level:
        ///     <c>[DwarfSurfaceProbe(…, ProbeKey = "…")]</c>, which refines that for ONE option or ONE constructor
        ///     overload, because an option bag asks eighteen different questions and one shape cannot answer them.
        ///     Supply is <see cref="Contracts.SurfaceFixtures" />; the bijection holds against the UNION of both
        ///     demand sources, not either alone.
        ///     <para>
        ///         The case-level source used to be a hand-written option → key map in <c>OptionCatalog</c>. It is
        ///         now the same declarations read here, so there is one place a demand can be stated and the
        ///         option matrix and the surface matrix cannot point at different shapes for one option.
        ///     </para>
        /// </summary>
        [Fact]
        public void Every_declared_ProbeKey_binds_to_exactly_one_fixture()
        {
            var elementDemand = SurfaceCatalog.Elements
                .Where(e => e.ProbeKey is not null)
                .Select(e => (Key: e.ProbeKey!, Source: "element " + e.UsageName));

            var caseDemand = SurfaceCatalog.Elements
                .SelectMany(e => e.ProbeClaims.Select(p => (Element: e, Claim: p)))
                .Where(x => x.Claim.ProbeKey is not null)
                .Select(x => (Key: x.Claim.ProbeKey!,
                    Source: $"{x.Element.UsageName}." + (x.Claim.Property ?? $"ctor({x.Claim.ConstructorArity})")));

            var demandSources = elementDemand.Concat(caseDemand)
                .GroupBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => string.Join(" & ", g.Select(x => x.Source)), StringComparer.Ordinal);

            var demanded = demandSources.Keys.ToHashSet(StringComparer.Ordinal);
            var supplied = SurfaceFixtures.All.Keys.ToHashSet(StringComparer.Ordinal);

            var unbound = demanded.Except(supplied).OrderBy(k => k, StringComparer.Ordinal)
                .Select(k => $"{k} (demanded by {demandSources[k]})")
                .ToList();
            Assert.True(unbound.Count == 0,
                "ProbeKey(s) declared with no fixture in SurfaceFixtures: " +
                string.Join(", ", unbound) +
                ". The declaration states a demand — either an element's [DwarfSurface(ProbeKey = ...)] or a " +
                "case's [DwarfSurfaceProbe(..., ProbeKey = ...)] — and the fixture is the supply. An unbound " +
                "key means that case's probe silently falls back to the flat DTO pair, which cannot trigger " +
                "it, and the cell reads 'no effect' while the feature works perfectly.");

            var orphaned = supplied.Except(demanded).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.True(orphaned.Count == 0,
                "Fixture(s) in SurfaceFixtures claimed by no [DwarfSurface(ProbeKey = ...)] and no " + "[DwarfSurfaceProbe(ProbeKey = ...)] refinement: " + string.Join(", ", orphaned) + ". An orphaned fixture is dead weight that reads as coverage.");
        }

        /// <summary>
        ///     Every <c>[DwarfSurfaceProbe]</c> refinement is well-formed: it names a property or a constructor
        ///     arity the element actually has, no two refine the same case, it states something, and any declared
        ///     argument list names members and types the fixture in play really declares.
        ///     <para>
        ///         A refinement that governs no case is worse than a missing one. It reads as a reviewed decision
        ///         about a shape while the cases it names go on being probed against one that cannot ask them
        ///         anything, which is precisely the reading this whole mechanism exists to stop producing.
        ///     </para>
        /// </summary>
        [Fact]
        public void Every_declared_probe_refinement_is_well_formed()
        {
            var problems = SurfaceCatalog.Elements
                .SelectMany(e => SurfaceCatalog.ValidateProbeClaims(
                    e.UsageName,
                    SurfaceCatalog.DomainSizesOf(e),
                    SurfaceCatalog.ConstructorAritiesOf(e),
                    e.ProbeKey,
                    e.ProbeClaims,
                    SurfaceCatalog.FixtureNames))
                .ToList();

            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        /// <summary>
        ///     Feeds <c>ValidateProbeClaims</c> each malformed shape directly, for the same reason
        ///     <see cref="The_site_override_validator_rejects_every_malformed_shape" /> does: against the
        ///     well-formed declarations the repository actually has, every branch of that validator passes
        ///     vacuously, and a gate that has never fired is unverified code.
        /// </summary>
        [Fact]
        public void The_probe_refinement_validator_rejects_every_malformed_shape()
        {
            var domains = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Flag"] = 1,
                ["Mode"] = 3
            };
            int[] arities = [0, 1];
            var fixtureNames = new Dictionary<string, (IReadOnlySet<string>, IReadOnlySet<string>)>(
                StringComparer.Ordinal)
            {
                ["shape"] = (new HashSet<string>(StringComparer.Ordinal)
                    {
                        "Extra"
                    },
                    new HashSet<string>(StringComparer.Ordinal)
                    {
                        "Src",
                        "Dst"
                    })
            };

            string[] Check(params SurfaceProbeClaim[] cs)
            {
                return SurfaceCatalog.ValidateProbeClaims("Probe",
                    domains,
                    arities,
                    "element-key",
                    cs,
                    key => key is not null && fixtureNames.TryGetValue(key, out var n) ? n : null).ToArray();
            }

            static SurfaceProbeClaim Property(
                string name,
                string? key = null,
                string? value = null,
                string? args = null,
                string? options = null,
                string? unmeasured = null)
            {
                return new SurfaceProbeClaim(name, DwarfSurfaceProbeAttribute.NotAConstructor, key, value, args, options, unmeasured);
            }

            static SurfaceProbeClaim Ctor(
                int arity,
                string? key = null,
                string? value = null,
                string? args = null,
                string? options = null,
                string? unmeasured = null)
            {
                return new SurfaceProbeClaim(null, arity, key, value, args, options, unmeasured);
            }

            // A property the element does not have: governs no case.
            Assert.Contains("governs no case at all",
                Assert.Single(Check(Property("Nope", "shape"))),
                StringComparison.Ordinal);

            // Two refinements of one property.
            Assert.Contains("both refine 'Flag'",
                Assert.Single(Check(
                    Property("Flag", "shape"),
                    Property("Flag", "shape"))),
                StringComparison.Ordinal);

            // A constructor arity the element does not declare.
            Assert.Contains("does not declare",
                Assert.Single(Check(Ctor(7, "shape"))),
                StringComparison.Ordinal);

            // Two refinements of one constructor overload.
            Assert.Contains("never consulted",
                Assert.Single(Check(
                    Ctor(1, args: "{Extra}"),
                    Ctor(1, args: "{Extra}"))),
                StringComparison.Ordinal);

            // States nothing at all.
            Assert.Contains("refines nothing", Assert.Single(Check(Property("Flag"))), StringComparison.Ordinal);

            // Restates the element's own key.
            Assert.Contains("restates the element's own ProbeKey",
                Assert.Single(Check(Property("Flag", "element-key"))),
                StringComparison.Ordinal);

            // Arguments on a property case, and a Value on a constructor case: each goes nowhere.
            Assert.Contains("only a CONSTRUCTOR case has",
                Assert.Single(Check(Property("Flag", args: "{Extra}"))),
                StringComparison.Ordinal);
            Assert.Contains("only a PROPERTY case has",
                Assert.Single(Check(Ctor(1, value: "1"))),
                StringComparison.Ordinal);

            // A stated Value would drop the other two members of a three-member domain.
            Assert.Contains("has 3 members",
                Assert.Single(Check(Property("Mode", value: "Mode.X"))),
                StringComparison.Ordinal);

            // A placeholder naming a member the fixture does not declare, and a typeof naming a type it does not.
            Assert.Contains("names member 'Gone'",
                Assert.Single(Check(Ctor(1, "shape", args: "{Gone}"))),
                StringComparison.Ordinal);
            Assert.Contains("names type 'Missing'",
                Assert.Single(Check(Ctor(1, "shape", args: "typeof(Missing)"))),
                StringComparison.Ordinal);

            // Unmeasured: blank, on a property, on a non-bare constructor, and combined with a shape.
            Assert.Contains("states no reason",
                Assert.Single(Check(Ctor(0, unmeasured: "  "))),
                StringComparison.Ordinal);
            Assert.Contains("not the zero-argument constructor",
                Assert.Single(Check(Ctor(1, unmeasured: "why"))),
                StringComparison.Ordinal);
            Assert.Contains("not the zero-argument constructor",
                Assert.Single(Check(Property("Flag", unmeasured: "why"))),
                StringComparison.Ordinal);
            Assert.Contains("both unmeasurable and given a shape",
                Assert.Single(Check(Ctor(0, "shape", unmeasured: "why"))),
                StringComparison.Ordinal);

            // An element with no writable properties has nothing BUT its bare case, so excusing it excuses all.
            Assert.Contains("excusing it excuses every question",
                Assert.Single(SurfaceCatalog.ValidateProbeClaims("Probe",
                    new Dictionary<string, int>(StringComparer.Ordinal),
                    arities,
                    null,
                    [Ctor(0, unmeasured: "why")],
                    _ => null)),
                StringComparison.Ordinal);

            // The well-formed shapes the repository actually uses report nothing.
            Assert.Empty(Check(Property("Flag", "shape"),
                Property("Mode", "shape"),
                Ctor(1, args: "{Extra}, typeof(Dst)"),
                Ctor(0, unmeasured: "the bare form configures nothing")));
        }

        /// <summary>
        ///     A <c>[DwarfSurfaceProbe]</c> on a type with no <c>[DwarfSurface]</c> has no case-space to refine,
        ///     and <c>SurfaceCatalog</c> filters the type out of the element set entirely — so the refinement, and
        ///     every case it was written to shape, would vanish without a word. Same reasoning as
        ///     <see cref="No_site_override_sits_on_a_type_that_declares_no_category" />.
        /// </summary>
        [Fact]
        public void No_probe_refinement_sits_on_a_type_that_declares_no_category()
        {
            var orphans = typeof(DwarfMapperAttribute).Assembly.GetExportedTypes()
                .Where(t => t.GetCustomAttributes<DwarfSurfaceProbeAttribute>(false).Any() && t.GetCustomAttribute<DwarfSurfaceAttribute>(false) is null)
                .Select(t => t.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.True(orphans.Count == 0,
                "Type(s) carrying [DwarfSurfaceProbe] but no [DwarfSurface]: " + string.Join(", ", orphans) + ". The type is not in the catalogue at all, so neither the refinement nor the cases it shapes " + "are ever looked at.");
        }

        /// <summary>
        ///     The refinements are load-bearing, not decoration: several cases of one element must resolve
        ///     DIFFERENT fixtures. If every refinement were deleted — or <c>BuildCases</c> stopped consulting them
        ///     — this fails, rather than the option bags quietly returning to one flat pair for eighteen options
        ///     and ~150 cells changing verdict with nothing to say so.
        /// </summary>
        [Fact]
        public void At_least_one_element_probes_its_cases_against_different_fixtures()
        {
            var shapes = SurfaceCatalog.CrossProductElements
                .Select(e => (e.UsageName, Distinct: SurfaceCatalog.CasesFor(e)
                    .Select(c => c.ProbeKey).Distinct().Count()))
                .Where(x => x.Distinct > 1)
                .ToList();

            Assert.True(shapes.Count >= 2 && shapes.Max(x => x.Distinct) >= 10,
                "Only " +
                shapes.Count +
                " element(s) measure their cases against more than one fixture " +
                $"(widest: {(shapes.Count == 0 ? 0 : shapes.Max(x => x.Distinct))}). [DwarfMapper] and " +
                "[DwarfMapperDefaults] each demand a dozen-odd different shapes, one per option. Fewer means " +
                "either the refinements were deleted or BuildCases stopped reading them, and the option bags " +
                "are back to being asked eighteen questions with one flat DTO pair.");
        }
    }
}
