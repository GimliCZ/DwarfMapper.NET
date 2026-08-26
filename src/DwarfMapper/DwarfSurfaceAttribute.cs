// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper
{
    /// <summary>
    ///     What kind of surface an element is, and therefore what must be proved about it.
    ///     <para>
    ///         There is deliberately no <c>Exempt</c> member. Every category below carries a DIFFERENT mandatory
    ///         obligation; classifying an element redirects its proof rather than waiving it. This replaces six
    ///         independent allowlist dictionaries, each of which was one person typing a reason once.
    ///     </para>
    /// </summary>
    /// <summary>
    ///     What a surface element is security-relevant FOR, and therefore what must be documented and pinned
    ///     about it.
    ///     <para>
    ///         A separate axis from <see cref="SurfaceCategory" /> rather than more categories, because the two
    ///         questions are independent: <c>[Reinterpret]</c> is a <c>ConsumerDirective</c> whose proof
    ///         obligation is the executed cross-product, AND a memory-safety override whose obligation is a
    ///         refusal test. Folding them would force one to be chosen over the other.
    ///     </para>
    ///     <para>
    ///         <see cref="FlagsAttribute" /> because one element can be several: <c>[DwarfMapper]</c> carries
    ///         both <c>AllowNonPublic</c> (a trust boundary) and <c>MaxDepth</c> (a resource bound).
    ///     </para>
    ///     <para>
    ///         Every non-<see cref="None" /> value carries an obligation, matching the rule
    ///         <see cref="SurfaceCategory" /> states: classifying redirects the proof, it never waives it.
    ///         Enforced by <c>SecuritySurfaceObligationTests</c>.
    ///     </para>
    /// </summary>
    [Flags]
    internal enum SecuritySurface
    {
        /// <summary>No security consequence. See the detector note on <see cref="DwarfSurfaceAttribute.Security" />.</summary>
        None = 0,

        /// <summary>
        ///     Widens what the generator may bind to, or who may contribute a mapping. Obligation: a
        ///     <c>SECURITY.md</c> trust-model section naming the boundary, and a test pinning the invariant.
        /// </summary>
        TrustBoundary = 1,

        /// <summary>
        ///     Lets the consumer override a compile-time proof about memory layout. Obligation: a test proving
        ///     the unsafe path is unreachable without its proof.
        /// </summary>
        MemorySafety = 2,

        /// <summary>
        ///     Sets or influences a resource limit — the guard against unbounded work. Obligation: the bound is
        ///     pinned by a test, and every site enforcing it is proven to agree.
        /// </summary>
        ResourceBound = 4
    }

    internal enum SurfaceCategory
    {
        /// <summary>
        ///     Hand-written by consumers and changes emitted code. Obligation: the executed cross-product (every
        ///     cell honoured, refused, or declared inapplicable), plus at least one consumer-assembly use and one
        ///     runnable-sample use.
        /// </summary>
        ConsumerDirective,

        /// <summary>
        ///     Emitted BY the generator onto the assembly; never hand-written (DWARF086 refuses hand-written use).
        ///     Obligation: produced by a generator run in a test AND consumed by the reading side.
        /// </summary>
        GeneratorEmitted,

        /// <summary>
        ///     Its whole observable effect is a build failure, so a passing sample cannot contain it. Obligation:
        ///     a NegativeCases row pinning the id AND its remedy wording, plus a positive row proving the
        ///     non-failing path compiles.
        /// </summary>
        BuildFailureOnly,

        /// <summary>
        ///     Changes the SHAPE or accessibility of generated code rather than runtime behaviour. Obligation: a
        ///     structural assertion over the generated text at every claimed endpoint.
        /// </summary>
        EmissionShape,

        /// <summary>
        ///     Only observable across an assembly boundary. Obligation: exercised by a multi-assembly consumer
        ///     fixture; single-assembly cells are inapplicable by category, not by allowlist.
        /// </summary>
        CrossAssembly,

        /// <summary>
        ///     Part of the testing surface consumers use to write their own tests. Obligation: consumer-shaped use
        ///     plus a DwarfMapper.Testing.Tests contract row.
        /// </summary>
        TestingOnly
    }

    /// <summary>
    ///     The mapping shapes a surface element can reach. Member names are kept identical to
    ///     <c>DwarfMapper.Generator.Tests.Contracts.Endpoint</c>; a test asserts the bijection, because two enums
    ///     that drift apart would silently repoint every claim at the wrong endpoint.
    /// </summary>
    [Flags]
    internal enum SurfaceEndpoints
    {
        None = 0,
        CreateMap = 1,
        UpdateInto = 2,
        Projection = 4,
        SpanMap = 8,
        AsyncStream = 16,
        Registry = 32,
        CoLocatedHost = 64,

        /// <summary>
        ///     Every endpoint. This is the DEFAULT on purpose. Over-claiming fails (a claimed endpoint must be
        ///     honoured or refused) and under-claiming fails too (an unclaimed endpoint must be silent or
        ///     uncompilable), so there is no value of <see cref="DwarfSurfaceAttribute.AppliesTo" /> that passes
        ///     vacuously — a wrong default is always caught rather than quietly ratified.
        /// </summary>
        All = CreateMap | UpdateInto | Projection | SpanMap | AsyncStream | Registry | CoLocatedHost
    }

    /// <summary>
    ///     Declares what must be proved about a public surface element, at the element's own declaration.
    ///     <para>
    ///         Two facts about a surface element cannot be reflected: what SHAPE makes it observable, and which
    ///         endpoints it legitimately does not reach. Both used to live in hand-kept dictionaries in the test
    ///         project, where nothing forced a new element to acquire an entry — an unprobed element read as "not
    ///         probed" and claimed nothing. Here they sit next to the code they describe and are verified in both
    ///         directions.
    ///     </para>
    ///     <para>
    ///         Internal, and read only by the test projects via <c>[InternalsVisibleTo]</c>. Nothing in the shipped
    ///         runtime reads it: this is metadata, and the package's zero-reflection, trim- and AOT-safe guarantees
    ///         are unaffected.
    ///     </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
    internal sealed class DwarfSurfaceAttribute : Attribute
    {
        public DwarfSurfaceAttribute(SurfaceCategory category)
        {
            Category = category;
        }

        /// <summary>What kind of surface this is, and therefore what must be proved about it.</summary>
        public SurfaceCategory Category { get; }

        /// <summary>
        ///     The endpoints this element CLAIMS to affect, at every declaration site its <c>AttributeUsage</c>
        ///     permits. Verified in both directions by <c>SurfaceParityTests</c>: a claimed endpoint where the
        ///     element does nothing observable fails, and an unclaimed endpoint where it changes the output fails
        ///     too.
        ///     <para>
        ///         This is the DEFAULT claim, not the only one. An element whose reach genuinely differs by
        ///         declaration site narrows the site with <see cref="DwarfSurfaceSiteAttribute" />; see there for
        ///         why one value per element is not enough.
        ///     </para>
        /// </summary>
        public SurfaceEndpoints AppliesTo { get; set; } = SurfaceEndpoints.All;

        /// <summary>
        ///     Names the test-side fixture whose type shape makes this element observable — e.g. an enum pair with
        ///     divergent member order, or a self-referencing graph. Bound dynamically: the test project must
        ///     contain exactly one <c>[SurfaceProbe]</c> fixture with this key, and every fixture must be claimed
        ///     by at least one element. Null means the default flat DTO pair suffices.
        ///     <para>
        ///         This is the element-wide DEFAULT. An element whose individual cases need DIFFERENT shapes — an
        ///         option bag, where every property asks its own question — refines it per case with
        ///         <see cref="DwarfSurfaceProbeAttribute" />.
        ///     </para>
        /// </summary>
        public string? ProbeKey { get; set; }

        /// <summary>
        ///     What this element is security-relevant for. Default <see cref="SecuritySurface.None" />.
        ///     <para>
        ///         A default of <c>None</c> would normally be the shape that passes vacuously, which this
        ///         repository treats as the primary failure mode. It does not here, because the value is
        ///         DETECTED as well as declared: <c>SecuritySurfaceObligationTests</c> derives the expected
        ///         flags from the element's own shape — an attribute exposing <c>AllowNonPublic</c> must
        ///         declare <see cref="SecuritySurface.TrustBoundary" />, one exposing <c>MaxDepth</c> must
        ///         declare <see cref="SecuritySurface.ResourceBound" />, and the blit override must declare
        ///         <see cref="SecuritySurface.MemorySafety" /> — and asserts declared ⊇ detected.
        ///     </para>
        ///     <para>
        ///         The limit of that, stated rather than discovered later: it proves no KNOWN mechanism is
        ///         undeclared. A novel one goes undetected until someone adds a detector for it.
        ///     </para>
        /// </summary>
        public SecuritySurface Security { get; set; } = SecuritySurface.None;
    }

    /// <summary>
    ///     Refines <see cref="DwarfSurfaceAttribute.ProbeKey" /> — and supplies the argument or value that
    ///     actually bites — for ONE case of an element, because one fixture per element cannot pose one question
    ///     per option.
    ///     <para>
    ///         The forcing case is <c>[DwarfMapper]</c>. It has eighteen writable properties and each needs a
    ///         different shape to become observable: <c>EnumStrategy</c> needs two enums in divergent member
    ///         order, <c>MaxDepth</c> needs a recursive graph, <c>NullCollections</c> needs different collection
    ///         types on each side. Probed against one flat DTO pair, every one of them reads "no effect" while the
    ///         option works perfectly — a hundred and fifty cells of the matrix reporting silence that the
    ///         instrument, not the generator, produced.
    ///     </para>
    ///     <para>
    ///         The second forcing case is the constructor argument. The matrix samples plausible literals, which
    ///         yields non-questions: <c>[AutoNest(true)]</c> restates the ambient default and therefore changes
    ///         nothing by construction, and <c>[MapIgnoreSource("Id")]</c> names a member that is already
    ///         consumed, so ignoring it is a no-op. A cell that reads silent because its argument was meaningless
    ///         is indistinguishable in the output from a real silent divergence, so the argument list that bites
    ///         is stated here. Member names are written as <c>{Member}</c> placeholders, which the test project
    ///         expands to a quoted literal AFTER checking the name against the fixture actually in play — an
    ///         argument that stops naming a real member fails a gate instead of quietly degrading to a no-op cell.
    ///     </para>
    ///     <para>
    ///         Applying more than one to the same property or the same constructor arity is an error rather than a
    ///         silent no-op, as is naming a property or an arity the element does not have, or restating the
    ///         element's own <see cref="DwarfSurfaceAttribute.ProbeKey" />. All are asserted by
    ///         <c>SurfaceDeclarationTests</c>, on the same reasoning as <see cref="DwarfSurfaceSiteAttribute" />:
    ///         a stale refinement reads as a reviewed decision while governing nothing.
    ///     </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum, AllowMultiple = true, Inherited = false)]
    internal sealed class DwarfSurfaceProbeAttribute : Attribute
    {
        /// <summary>The <see cref="ConstructorArity" /> of a probe written in the PROPERTY form.</summary>
        public const int NotAConstructor = -1;

        /// <summary>Refines the cases that vary one writable property of this element.</summary>
        /// <param name="property">
        ///     The property's name, which must be a public readable/writable non-indexer property of the element —
        ///     anything else governs zero cases.
        /// </param>
        public DwarfSurfaceProbeAttribute(string property)
        {
            Property = property;
            ConstructorArity = NotAConstructor;
        }

        /// <summary>Refines the case that exercises one public constructor overload of this element.</summary>
        /// <param name="constructorArity">
        ///     The overload's parameter count, which is how <c>SurfaceCatalog</c> labels a constructor case. The
        ///     element must declare exactly one public constructor with that many parameters.
        /// </param>
        public DwarfSurfaceProbeAttribute(int constructorArity)
        {
            ConstructorArity = constructorArity;
        }

        /// <summary>The writable property whose cases this refines, or null in the constructor form.</summary>
        public string? Property { get; }

        /// <summary>
        ///     The constructor overload's parameter count, or <see cref="NotAConstructor" /> in the property form.
        ///     An <c>int?</c> is not a legal attribute argument type, so the property form carries the sentinel.
        /// </summary>
        public int ConstructorArity { get; }

        /// <summary>
        ///     The fixture whose shape makes THIS case observable, overriding the element's own
        ///     <see cref="DwarfSurfaceAttribute.ProbeKey" />. Bound to a <c>[SurfaceProbe]</c> fixture by the same
        ///     bijection the element-level key uses: every declared key must have exactly one fixture, and every
        ///     fixture must be claimed.
        /// </summary>
        public string? ProbeKey { get; set; }

        /// <summary>
        ///     Property form only: the initialiser value, replacing the one the matrix would derive. Needed where
        ///     no amount of reflection reveals what a value MEANS — the generic rule for an <c>int</c> is "step
        ///     it", which turns a <c>MaxDepth</c> default of 64 into 65 and binds on nothing, because nothing
        ///     about the type says it is a budget rather than a count. Only legal where the derived domain holds a
        ///     single value, so it can never silently drop members of an enum's full domain.
        /// </summary>
        public string? Value { get; set; }

        /// <summary>
        ///     Constructor form only: the argument list as written in source, with <c>{Member}</c> placeholders
        ///     for member names of the fixture in play. <c>"{Extra}"</c> renders as <c>"Extra"</c> and fails a gate
        ///     if the fixture stops declaring a member of that name; <c>typeof(X)</c> is checked the same way
        ///     against the fixture's declared types. Everything else passes through verbatim, which is how a
        ///     literal that is deliberately NOT a member — a constant value, a converter name — stays unchecked
        ///     and visibly so.
        /// </summary>
        public string? Arguments { get; set; }

        /// <summary>
        ///     The <c>[DwarfMapper(...)]</c> options the surrounding mapper must carry for this case to have
        ///     anything to do — the ambient conditions under which the directive is even reachable.
        ///     <para>
        ///         <c>[MapIgnoreSource]</c> is the forcing case: its entire effect is to silence the source-coverage
        ///         suggestion, which is only raised under <c>RequiredMapping = Both</c>. Probed against a mapper
        ///         with the default strategy there is no suggestion to silence, so the directive correctly does
        ///         nothing and the cell reads as a divergence the generator never committed. No reflection over
        ///         the attribute reveals which option switches its own effect on.
        ///     </para>
        ///     <para>
        ///         Applied to the mapper class the endpoint template generates, so it reaches neither the registry
        ///         nor the co-located host (neither declares one) — a cell where the options did not arrive is
        ///         counted as unasked rather than read as silence.
        ///     </para>
        /// </summary>
        public string? MapperOptions { get; set; }

        /// <summary>
        ///     Declares that this case cannot pose a question at all, and why. The matrix excuses its silent cells
        ///     and COUNTS them against a shrink-only ceiling, so the hole is declared and visible rather than
        ///     reported as a divergence the generator never committed.
        ///     <para>
        ///         Legal only on the zero-argument constructor of an element that has writable properties — an
        ///         option BAG whose bare form sets nothing. That restriction is the point: it is structurally
        ///         impossible to mark a case that actually says something as unmeasurable, so this cannot become
        ///         the hatch that turns a live divergence green.
        ///     </para>
        /// </summary>
        public string? Unmeasured { get; set; }
    }

    /// <summary>
    ///     Assigns a <see cref="SurfaceCategory" /> to ONE writable property of an option-bag element, because a
    ///     category is declared per TYPE and the properties of an option bag are not all the same kind of surface.
    ///     <para>
    ///         The forcing case is <c>[DwarfMapper]</c>, whose eighteen options include at least two that are not
    ///         consumer-demonstrable in the way the element as a whole is.
    ///         <c>ImplicitConversions = false</c> turns a warning into a BUILD ERROR, so the sample that would
    ///         demonstrate the difference could not compile; <c>GenerateExtensions = false</c> has no runtime
    ///         effect at all — its whole observable result is the ABSENCE of generated extension methods.
    ///         Both used to sit in a test-side <c>NotDemonstrable</c> dictionary as one-line excuses. Here they
    ///         are redirected: each names the category whose obligation it must satisfy INSTEAD, and that
    ///         obligation runs.
    ///     </para>
    ///     <para>
    ///         Not folded into <see cref="DwarfSurfaceProbeAttribute" />, which also addresses individual
    ///         properties. An enum is not nullable in attribute metadata, so a category property there would need
    ///         an "unset" member — which is the <c>Exempt</c> member this design refuses, under another name. A
    ///         separate attribute makes "this property's category was set" the presence of a declaration rather
    ///         than a sentinel value.
    ///     </para>
    ///     <para>
    ///         Naming a property the element does not have, declaring the same property twice, or stating no
    ///         reason are all errors rather than silent no-ops, asserted by <c>SurfaceDeclarationTests</c> — a
    ///         redirect that governs nothing reads as a reviewed decision while the option it names falls back to
    ///         an obligation nobody chose for it.
    ///     </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum, AllowMultiple = true, Inherited = false)]
    internal sealed class DwarfSurfaceOptionAttribute : Attribute
    {
        public DwarfSurfaceOptionAttribute(string option, SurfaceCategory category, string because)
        {
            Option = option;
            Category = category;
            Because = because;
        }

        /// <summary>
        ///     The writable property this redirects. Must be a public readable/writable non-indexed property of
        ///     the element — anything else redirects nothing.
        /// </summary>
        public string Option { get; }

        /// <summary>The category whose obligation this option must satisfy, in place of the element's own.</summary>
        public SurfaceCategory Category { get; }

        /// <summary>
        ///     Why this option's proof lives somewhere other than the element's. Mandatory and asserted non-blank,
        ///     for the same reason <see cref="DwarfSurfaceSiteAttribute.Because" /> is: an unexplained redirect is
        ///     an allowlist entry with a category name on it.
        /// </summary>
        public string Because { get; }
    }

    /// <summary>
    ///     Narrows <see cref="DwarfSurfaceAttribute.AppliesTo" /> for ONE declaration site, because an element's
    ///     reach is not always uniform across the sites its <c>AttributeUsage</c> permits.
    ///     <para>
    ///         The forcing case is <c>[MapProperty]</c>. On a mapping METHOD it is a mapper directive; on a DTO
    ///         MEMBER it is the <c>[MapTo]</c> registry form, where the annotated member supplies the destination.
    ///         Those are two different features that happen to share a name, and they reach different endpoints:
    ///         at <c>CreateMap</c> the method form is refused with DWARF038 while the member form has nothing to
    ///         attach to at all. No single value of <see cref="DwarfSurfaceAttribute.AppliesTo" /> satisfies both
    ///         cells — claim <c>CreateMap</c> and the member cell fails as over-reach, drop it and the method cell
    ///         fails as under-reach. Roughly a hundred cells sat in that hole, and they were held by a predicate in
    ///         the test project: exactly the hand-kept, test-side knowledge this architecture exists to delete,
    ///         governing cells nobody reviewed and forcing no entry on a newly added element.
    ///     </para>
    ///     <para>
    ///         <see cref="Because" /> is a CONSTRUCTOR argument rather than an optional property so a narrowing
    ///         cannot be recorded without saying why, and the reason must be about the SHAPE of the site. "The
    ///         generator does not read it there" is a divergence to report, not a claim to encode: narrowing to
    ///         keep a cell green converts a live bug into documented intended behaviour, permanently and invisibly.
    ///     </para>
    ///     <para>
    ///         Applying more than one to the same site, or naming a site the element's <c>AttributeUsage</c> does
    ///         not permit, is an error rather than a silent no-op — a stale override is how a claim quietly stops
    ///         applying. So is restating the element's own default, which reads as a reviewed decision while
    ///         narrowing nothing. All four are asserted by <c>SurfaceDeclarationTests</c>.
    ///     </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum, AllowMultiple = true, Inherited = false)]
    internal sealed class DwarfSurfaceSiteAttribute : Attribute
    {
        public DwarfSurfaceSiteAttribute(AttributeTargets site, SurfaceEndpoints appliesTo, string because)
        {
            Site = site;
            AppliesTo = appliesTo;
            Because = because;
        }

        /// <summary>
        ///     The declaration site(s) this claim replaces the element's default for. May combine flags
        ///     (<c>Property | Field</c>) when one reason covers several sites; every flag must be one the element's
        ///     own <c>AttributeUsage.ValidOn</c> permits.
        /// </summary>
        public AttributeTargets Site { get; }

        /// <summary>The endpoints the element claims to affect WHEN WRITTEN AT <see cref="Site" />.</summary>
        public SurfaceEndpoints AppliesTo { get; }

        /// <summary>
        ///     Why the element cannot reach the dropped endpoints from this site, stated in terms of the shape of
        ///     the site. Mandatory, and asserted non-blank.
        /// </summary>
        public string Because { get; }
    }
}
