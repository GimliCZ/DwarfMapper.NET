// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using System.Text.RegularExpressions;
using DwarfMapper;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     One obligation per category. There is deliberately no category with no obligation: this file is what
///     replaced six allowlist dictionaries, and it replaced them by REDIRECTING each excuse into a different
///     demand rather than by accepting it.
///     <para>
///         "Cannot be shown in a runnable sample because its effect is a build error" used to be a waiver.
///         Here it is <see cref="SurfaceCategory.BuildFailureOnly" />, which demands a NegativeCases row
///         pinning the id and its remedy wording. "Never hand-written" used to be a waiver; here it is
///         <see cref="SurfaceCategory.GeneratorEmitted" />, which demands a test asserting the generator
///         emits it. Every waiver became a demand, and every demand below runs.
///     </para>
///     <para>
///         The chain that makes this exhaustive has three links, and all three are asserted:
///         <c>SurfaceDeclarationTests.Every_public_attribute_declares_a_DwarfSurface_category</c> forces every
///         shipped attribute to carry a category, <see cref="Every_category_carries_an_obligation" /> forces
///         every category to appear below, and the theories below force every element of every category to
///         satisfy one. That chain is what supersedes <c>AssemblyScanTests.Scan4</c>, which asserted the same
///         thing far more weakly: a bare <c>Contains(usageName)</c> over the test sources, which a doc-comment
///         mentioning the attribute satisfied.
///     </para>
///     <para>
///         <b>What these obligations are, stated honestly.</b> They are substring scans over corpora, and so
///         are deliberately weaker than the executed matrix next door: "this attribute is used where a
///         consumer would use it" is a different claim from "this attribute behaves correctly", and
///         <c>SurfaceParityTests</c> already carries the second by running the generator over every cell.
///         What a scan can do — and what the deleted allowlists could not — is refuse to let an element sit at
///         zero presence with a one-line excuse next to it.
///     </para>
/// </summary>
public sealed class SurfaceObligationTests
{
    // ── The corpora ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The consumer-shaped assemblies: projects that USE DwarfMapper rather than test the generator.</summary>
    private static string ConsumerCorpus { get; } = Corpus(RepoPaths.ConsumerRoots);

    /// <summary>Everything under <c>samples/</c> — the code a reader can clone and run.</summary>
    private static string SampleCorpus { get; } = Corpus([RepoPaths.Samples]);

    /// <summary>The deliberately-red case files, each declaring the exact diagnostic set it provokes.</summary>
    private static string NegativeCorpus { get; } =
        Corpus([Path.Combine(RepoPaths.Tests, "DwarfMapper.NegativeCases")]);

    /// <summary>
    ///     The genuinely multi-assembly fixture: five projects, with the host deliberately holding no
    ///     compile-time reference to the providers. That last constraint is what makes it the only place in
    ///     the repository where a cross-assembly element can be exercised at all — <c>IntegrationTests</c> is
    ///     one csproj, so a "cross-assembly" row written there would be a single-assembly probe with a
    ///     cross-assembly name.
    /// </summary>
    private static string MultiAssemblyCorpus { get; } =
        Corpus([Path.Combine(RepoPaths.Tests, "DwarfMapper.ConsumerTests")]);

    /// <summary>
    ///     The generator tests, EXCLUDING this architecture's own scaffolding.
    ///     <para>
    ///         <c>Contracts/</c> and <c>SelfValidation/</c> are excluded because they are full of literal
    ///         element names written as bookkeeping — <c>DeclaredDivergences</c> names a dozen attributes,
    ///         and the very allowlists this task deletes named five more. A GeneratorEmitted obligation that
    ///         counted those would have been satisfied by the excuse it replaced, and would have gone red the
    ///         moment the excuse was deleted. The obligation has to be met by a test that runs the generator.
    ///     </para>
    /// </summary>
    private static string GeneratorTestCorpus { get; } = Corpus(
        [Path.Combine(RepoPaths.Tests, "DwarfMapper.Generator.Tests")],
        exclude: ["Contracts", "SelfValidation"]);

    /// <summary>The contract suite for the testing surface consumers use to write their own tests.</summary>
    private static string TestingContractCorpus { get; } =
        Corpus([Path.Combine(RepoPaths.Tests, "DwarfMapper.Testing.Tests")]);

    // ── Non-vacuity ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     A mistyped or moved path makes every obligation below pass by finding nothing to check — the same
    ///     failure mode <see cref="RepoPaths" /> exists to stop, asserted here because this file adds four
    ///     corpora of its own that <c>RepoPaths</c> does not know about.
    /// </summary>
    [Fact]
    public void Every_corpus_is_non_empty()
    {
        foreach (var root in RepoPaths.ConsumerRoots)
        {
            Assert.True(Directory.Exists(root), $"Consumer root '{root}' does not exist on disk.");
            Assert.NotEmpty(RepoPaths.SourceFiles(root));
        }

        Assert.False(string.IsNullOrWhiteSpace(ConsumerCorpus), "Consumer corpus is empty.");
        Assert.False(string.IsNullOrWhiteSpace(SampleCorpus), "Sample corpus is empty — check RepoPaths.Samples.");
        Assert.False(string.IsNullOrWhiteSpace(NegativeCorpus), "NegativeCases corpus is empty.");
        Assert.False(string.IsNullOrWhiteSpace(MultiAssemblyCorpus), "ConsumerTests corpus is empty.");
        Assert.False(string.IsNullOrWhiteSpace(GeneratorTestCorpus), "Generator-test corpus is empty.");
        Assert.False(string.IsNullOrWhiteSpace(TestingContractCorpus), "Testing.Tests corpus is empty.");
    }

    /// <summary>
    ///     The matcher itself, in both directions.
    ///     <para>
    ///         A scan is only as honest as its boundary. <c>Contains("[MapIgnore")</c> is satisfied by
    ///         <c>[MapIgnoreSource]</c>, <c>Contains("[Flatten")</c> by <c>[FlattenGraph]</c>, and
    ///         <c>Contains("[DwarfMapper")</c> by any of four prefixed siblings — so under the obvious
    ///         implementation an element's obligation is discharged by a DIFFERENT element's use, which is
    ///         exactly the shape of proof this task exists to delete. A too-loose matcher fails nothing and
    ///         is therefore invisible; hence the negative controls.
    ///     </para>
    /// </summary>
    [Fact]
    public void The_usage_matcher_distinguishes_an_element_from_its_prefixed_siblings()
    {
        Assert.True(IsWritten("[MapIgnore]", "MapIgnore"));
        Assert.True(IsWritten("[MapIgnore(nameof(Dto.Id))]", "MapIgnore"));
        Assert.True(IsWritten("[MapNullSkip<Src, Dst>(true)]", "MapNullSkip"));
        Assert.True(IsWritten("[assembly: DwarfMapperDefaults(AutoNest = false)]", "DwarfMapperDefaults"));
        Assert.True(IsWritten("[assembly: global::DwarfMapper.DwarfProvidesMap(typeof(A), typeof(B))]",
            "DwarfProvidesMap"));

        Assert.False(IsWritten("[MapIgnoreSource(\"Id\")]", "MapIgnore"));
        Assert.False(IsWritten("[FlattenGraph(\"Root\", \"Flat\")]", "Flatten"));
        Assert.False(IsWritten("[DwarfMapperOptions(PublicExtensions = true)]", "DwarfMapper"));
        Assert.False(IsWritten("see MapProperty for the two-argument form", "MapProperty"));
    }

    /// <summary>
    ///     Every member of <see cref="SurfaceCategory" /> is claimed by a theory below, AND by the
    ///     option-level resolver.
    ///     <para>
    ///         The load-bearing assertion of this file. Adding a category with no obligation would restore
    ///         exactly what the allowlists were — a place to put an element so that nothing is asked of it —
    ///         and it would do so silently, because an unclaimed category simply produces no theory rows.
    ///     </para>
    ///     <para>
    ///         Both levels are checked, because they can diverge. The element theories and
    ///         <see cref="CorpusFor" /> are separate lists of categories, so a seventh member could acquire an
    ///         obligation at element level and fall into a default arm at option level — where every option
    ///         redirected to it would be scanned against whatever corpus the arm happened to name. That is the
    ///         same hole one level down, which is why <see cref="CorpusFor" /> throws rather than defaulting.
    ///     </para>
    /// </summary>
    [Fact]
    public void Every_category_carries_an_obligation()
    {
        var withObligations = new[]
        {
            SurfaceCategory.ConsumerDirective, SurfaceCategory.GeneratorEmitted,
            SurfaceCategory.BuildFailureOnly, SurfaceCategory.EmissionShape,
            SurfaceCategory.CrossAssembly, SurfaceCategory.TestingOnly
        };

        var unclaimed = Enum.GetValues<SurfaceCategory>().Except(withObligations).ToList();

        Assert.True(unclaimed.Count == 0,
            "SurfaceCategory member(s) with no obligation in this file:\n  "
            + string.Join("\n  ", unclaimed)
            + "\n\nA category with no obligation is an allowlist with an enum member's name on it: an element "
            + "assigned to it is asked for nothing, and nothing says so. Add the theory, then add the member "
            + "to the list above.");

        // The option level, which the list above does not reach. A category CorpusFor cannot resolve throws,
        // so this fails loudly rather than silently scanning an option against an arbitrary corpus.
        foreach (var category in Enum.GetValues<SurfaceCategory>())
        {
            var (corpus, corpusName, remedy) = CorpusFor(category);
            Assert.False(string.IsNullOrWhiteSpace(corpus), $"{category}: option corpus is empty.");
            Assert.False(string.IsNullOrWhiteSpace(corpusName));
            Assert.False(string.IsNullOrWhiteSpace(remedy));
        }
    }

    /// <summary>
    ///     Reaches <see cref="CorpusFor" />'s throwing default arm (B10). Every one of
    ///     <c>SurfaceCategory</c>'s six members has an explicit arm, so the guard that makes
    ///     <see cref="Every_category_carries_an_obligation" /> load-bearing at the option level was itself
    ///     untested code: nothing proved it throws rather than, say, having been quietly turned into a
    ///     default that returns the generator-test corpus.
    ///     <para>
    ///         An undefined enum value is the only input that reaches it, and casting one is legal C#. The
    ///         message is asserted too, not just the throw: the arm's value is that it TELLS the next person
    ///         what to add, and a fail-fast whose message decayed to "unexpected value" would send them
    ///         hunting for the corpus list this arm exists to point at.
    ///     </para>
    /// </summary>
    [Fact]
    public void CorpusFor_refuses_a_category_it_cannot_resolve()
    {
        var unknown = (SurfaceCategory)9999;
        Assert.DoesNotContain(unknown, Enum.GetValues<SurfaceCategory>());

        var ex = Assert.Throws<InvalidOperationException>(() => CorpusFor(unknown));
        Assert.Contains("no option-level obligation", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Add an arm", ex.Message, StringComparison.Ordinal);
    }

    // ── The obligations, one per category ───────────────────────────────────────────────────────────

    public static TheoryData<string> ConsumerDirectives() => Of(SurfaceCategory.ConsumerDirective);
    public static TheoryData<string> GeneratorEmitted() => Of(SurfaceCategory.GeneratorEmitted);
    public static TheoryData<string> BuildFailureOnly() => Of(SurfaceCategory.BuildFailureOnly);
    public static TheoryData<string> EmissionShapes() => Of(SurfaceCategory.EmissionShape);
    public static TheoryData<string> CrossAssembly() => Of(SurfaceCategory.CrossAssembly);
    public static TheoryData<string> TestingOnly() => Of(SurfaceCategory.TestingOnly);

    [Theory]
    [MemberData(nameof(ConsumerDirectives))]
    public void A_consumer_directive_is_used_in_a_consumer_assembly_and_a_runnable_sample(string usageName)
    {
        Assert.True(IsWritten(ConsumerCorpus, usageName),
            $"[{usageName}] is a ConsumerDirective with NO use in any consumer-shaped assembly "
            + $"({string.Join(", ", RepoPaths.ConsumerRoots.Select(Path.GetFileName))}). Thirteen public "
            + "attributes sat in exactly this state, including the documented remedy for DWARF039. Add a "
            + "corpus row that STATES what it proves — the house convention is a doc-comment naming the "
            + "behaviour and an assertion that observes it, not a mention — or, if the element genuinely is "
            + "not consumer-authored, change its category and take on that category's obligation instead.");

        Assert.True(IsWritten(SampleCorpus, usageName),
            $"[{usageName}] is a ConsumerDirective demonstrated in NO runnable sample. Add a Conformance "
            + "feature (samples/DwarfMapper.Conformance) asserting its observable runtime difference, or a "
            + "Gallery example if it deserves prose. A doc-comment mentioning the name does not count: this "
            + "scan matches the attribute as WRITTEN, because the excuse it replaced was satisfied by prose.");
    }

    [Theory]
    [MemberData(nameof(BuildFailureOnly))]
    public void A_build_failure_only_element_has_a_pinned_diagnostic_row(string usageName)
    {
        Assert.True(IsWritten(NegativeCorpus, usageName),
            $"[{usageName}] is BuildFailureOnly — its whole observable effect is a diagnostic — but no "
            + "NegativeCases row exercises it. That category is not a waiver: it REDIRECTS the obligation "
            + "from 'demonstrate it in a sample' to 'pin the diagnostic and its remedy wording'. Add a case "
            + "file with `// EXPECT:` naming the exact id set and `// EXPECT-MESSAGE` pinning the remedy.");
    }

    [Theory]
    [MemberData(nameof(CrossAssembly))]
    public void A_cross_assembly_element_is_exercised_by_a_multi_assembly_fixture(string usageName)
    {
        Assert.True(IsWritten(MultiAssemblyCorpus, usageName),
            $"[{usageName}] is CrossAssembly — only observable across an assembly boundary — but no "
            + "multi-assembly fixture uses it. tests/DwarfMapper.ConsumerTests is the one place in the "
            + "repository with that shape (five projects; the host holds no compile-time reference to the "
            + "providers). A single-assembly probe validates nothing here, which is precisely why this "
            + "element is not on the executed cross-product either.");
    }

    [Theory]
    [MemberData(nameof(GeneratorEmitted))]
    public void A_generator_emitted_element_is_written_by_a_generator_run(string usageName)
    {
        Assert.True(IsWritten(GeneratorTestCorpus, usageName),
            $"[{usageName}] is GeneratorEmitted but no generator test asserts the emitted text contains it — "
            + "nothing proves the generator writes it, so the manifest could stop being emitted and the "
            + "reading side would simply find an empty set and validate nothing.\n\n"
            + "Note the corpus deliberately EXCLUDES Contracts/ and SelfValidation/: those name elements as "
            + "bookkeeping, and an obligation satisfied by the allowlist it replaced would go red the day "
            + "the allowlist was deleted.");
    }

    [Theory]
    [MemberData(nameof(EmissionShapes))]
    public void An_emission_shape_element_has_a_structural_assertion_over_the_generated_text(string usageName)
    {
        Assert.True(IsWritten(GeneratorTestCorpus, usageName),
            $"[{usageName}] is EmissionShape — it changes the shape or accessibility of generated code "
            + "rather than runtime behaviour — but no generator test writes it. A running sample cannot show "
            + "an accessibility change or the ABSENCE of a generated member, which is exactly why this "
            + "category exists; what it demands instead is an assertion over the emitted text.");
    }

    [Theory]
    [MemberData(nameof(TestingOnly))]
    public void A_testing_only_element_is_used_by_a_consumer_and_pinned_by_the_testing_contract(string usageName)
    {
        Assert.True(IsWritten(ConsumerCorpus, usageName),
            $"[{usageName}] is TestingOnly — part of the surface consumers use to write THEIR tests — but no "
            + "consumer-shaped assembly writes it. A testing helper nobody outside the package has used is a "
            + "helper whose ergonomics have never been measured.");

        // IsWritten, not Contains: `RoundTripException` satisfies a bare Contains("RoundTrip"), which is the
        // prefix collision this file's own negative controls exist to catch. It passes today on a real
        // [RoundTrip], so this was hygiene rather than breakage — but a scan that CAN be discharged by a
        // neighbouring type name is the failure mode, not an instance of it.
        Assert.True(IsWritten(TestingContractCorpus, usageName),
            $"[{usageName}] is TestingOnly but tests/DwarfMapper.Testing.Tests never writes it. The "
            + "testing surface needs its own contract row: it ships to consumers, so a change to it is a "
            + "breaking change to them.");
    }

    // ── The same six obligations, one level down: the options of an option bag ──────────────────────

    /// <summary>
    ///     Every writable property of <c>[DwarfMapper]</c>, and the category whose obligation it carries.
    ///     <para>
    ///         A category is declared per TYPE, and <c>[DwarfMapper]</c> is one type carrying eighteen
    ///         independent options. The element-level obligation above is discharged by any one of them, so
    ///         without this theory the whole option bag would be proved by a single <c>[DwarfMapper]</c>
    ///         anywhere — and six of fifteen options once sat undemonstrated behind exactly that, including
    ///         <c>SkipNullSourceMembers</c>, the one a migrating consumer reaches for first.
    ///     </para>
    ///     <para>
    ///         The category comes from the declaration (<c>[DwarfSurfaceOption]</c>) or, for an option nobody
    ///         has redirected, from the element. There is no way to name no category, which is the difference
    ///         between this and the <c>NotDemonstrable</c> dictionary it replaced.
    ///     </para>
    /// </summary>
    public static TheoryData<string> ClassLevelOptions()
    {
        var data = new TheoryData<string>();
        foreach (var option in OptionNames) data.Add(option);
        return data;
    }

    [Theory]
    [MemberData(nameof(ClassLevelOptions))]
    public void A_class_level_option_satisfies_the_obligation_of_its_declared_category(string option)
    {
        var element = SurfaceCatalog.Elements
            .Single(e => e.Type == typeof(DwarfMapperAttribute));
        var category = SurfaceCatalog.CategoryOfOption(element, option);

        var (corpus, corpusName, remedy) = CorpusFor(category);

        Assert.True(IsAssigned(corpus, option),
            $"[DwarfMapper({option} = …)] is categorised {category} but is never WRITTEN in {corpusName}.\n\n"
            + remedy + "\n\nIf the option genuinely belongs to a different kind of proof, redirect it at the "
            + "declaration with [DwarfSurfaceOption(nameof(DwarfMapperAttribute." + option + "), "
            + "SurfaceCategory.X, \"why\")] and satisfy THAT obligation. There is no category that asks for "
            + "nothing — which is the whole difference between this and the NotDemonstrable dictionary it "
            + "replaced.");
    }

    [Fact]
    public void The_option_scan_is_not_vacuous()
    {
        Assert.True(OptionNames.Count >= 15,
            $"Only {OptionNames.Count} settable options found on [DwarfMapper]. If the surface really shrank, "
            + "lower this floor deliberately; otherwise the reflection stopped seeing the options and this "
            + "gate has gone vacuous.");

        // The assignment matcher, in both directions — a matcher that matched everything would discharge
        // every option's obligation against the prose that mentions it, which is the state this replaced.
        Assert.True(IsAssigned("[DwarfMapper(SkipNullSourceMembers = true)]", "SkipNullSourceMembers"));
        Assert.False(IsAssigned("// SkipNullSourceMembers is the switch", "SkipNullSourceMembers"));
        Assert.False(IsAssigned("nameof(DwarfMapperAttribute.SkipNullSourceMembers)", "SkipNullSourceMembers"));
    }

    // ── Machinery ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     The corpus one category's obligation is measured against at the OPTION level, with the name and
    ///     remedy for the message.
    ///     <para>
    ///         Every option is matched as an ASSIGNMENT (<c>Name =</c>) against comment-stripped text. Both
    ///         halves are load-bearing and both were breached by the excuses this replaced:
    ///         <c>SkipNullSourceMembers</c> appeared in <c>samples/</c> only inside a section header, and
    ///         <c>GenerateExtensions</c> only inside a prose line that happens to quote the assignment. A bare
    ///         <c>Contains</c> over raw text greened both while neither was ever written.
    ///     </para>
    ///     <para>
    ///         There is deliberately no default arm. A category this cannot resolve would otherwise be scanned
    ///         against whatever corpus the arm happened to name — an obligation nobody chose, silently — which
    ///         is the same hole as a category with no obligation, one level down.
    ///         <see cref="Every_category_carries_an_obligation" /> calls this for every enum member so the
    ///         throw is reached by a test rather than by a consumer of the matrix.
    ///     </para>
    /// </summary>
    private static (string Corpus, string Name, string Remedy) CorpusFor(SurfaceCategory category) =>
        category switch
        {
            SurfaceCategory.ConsumerDirective => (SampleCorpus, "a runnable sample under samples/",
                "Add a Conformance feature asserting the option's observable runtime difference, or a "
                + "Gallery example if it deserves prose."),
            SurfaceCategory.BuildFailureOnly => (NegativeCorpus, "a NegativeCases row",
                "Add a case file whose `// EXPECT:` names the exact diagnostic set the option provokes."),
            SurfaceCategory.EmissionShape => (GeneratorTestCorpus, "a generator test",
                "Assert over the emitted text — presence, absence or accessibility of the generated member."),
            SurfaceCategory.CrossAssembly => (MultiAssemblyCorpus, "the multi-assembly fixture",
                "Exercise it in tests/DwarfMapper.ConsumerTests, where an assembly boundary exists."),
            SurfaceCategory.TestingOnly => (TestingContractCorpus, "the testing contract suite",
                "Pin it in tests/DwarfMapper.Testing.Tests."),
            SurfaceCategory.GeneratorEmitted => (GeneratorTestCorpus, "a generator test",
                "Assert that a generator run emits it."),
            _ => throw new InvalidOperationException(
                $"SurfaceCategory.{category} has no option-level obligation. Add an arm naming the corpus an "
                + "option redirected to this category must be written in. Defaulting here would scan the "
                + "option against a corpus nobody chose for it, which is the allowlist this file deleted, "
                + "wearing an enum member's name.")
        };

    private static List<string> OptionNames { get; } =
        typeof(DwarfMapperAttribute).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanWrite: true, CanRead: true } && p.GetIndexParameters().Length == 0)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    ///     The usage names of one category's elements, DEDUPED.
    ///     <para>
    ///         Five elements share a usage name with a generic twin, and a substring scan cannot tell
    ///         <c>[MapNullSkip(true)]</c> from <c>[MapNullSkip&lt;S, T&gt;(true)]</c> in any case. Deduping
    ///         says so once rather than emitting two theory rows that ask the same question. The distinction
    ///         is not lost — <c>SurfaceParityTests</c> keys on arity precisely because it can tell them apart,
    ///         by running the generator over each.
    ///     </para>
    /// </summary>
    private static TheoryData<string> Of(SurfaceCategory category)
    {
        var data = new TheoryData<string>();
        foreach (var name in SurfaceCatalog.Elements.Where(e => e.Category == category)
                     .Select(e => e.UsageName).Distinct(StringComparer.Ordinal)
                     .OrderBy(n => n, StringComparer.Ordinal))
            data.Add(name);
        return data;
    }

    /// <summary>
    ///     Whether the corpus WRITES this attribute: an opening bracket, an optional <c>assembly:</c> target
    ///     and an optional <c>global::DwarfMapper.</c> qualification (the form the generator emits), the exact
    ///     name, and then a character that can only follow a complete attribute name.
    /// </summary>
    private static bool IsWritten(string corpus, string usageName) =>
        Regex.IsMatch(corpus,
            @"\[\s*(assembly\s*:\s*)?(global\s*::\s*)?(DwarfMapper\s*\.\s*)?"
            + Regex.Escape(usageName) + @"\s*[\]\(<]",
            RegexOptions.None, TimeSpan.FromSeconds(30));

    /// <summary>
    ///     Whether the corpus ASSIGNS this option — <c>Name = value</c> — rather than merely naming it.
    ///     <c>nameof(X.Name)</c> is excluded by requiring the name not to be preceded by a dot.
    /// </summary>
    private static bool IsAssigned(string corpus, string option) =>
        Regex.IsMatch(StripComments(corpus), @"(?<![.\w])" + Regex.Escape(option) + @"\s*=(?!=)",
            RegexOptions.None, TimeSpan.FromSeconds(30));

    /// <summary>Concatenates every source file under each root, minus comments.</summary>
    private static string Corpus(IEnumerable<string> roots, IReadOnlyList<string>? exclude = null)
    {
        var sep = Path.DirectorySeparatorChar;
        return string.Concat(roots
            .SelectMany(RepoPaths.SourceFiles)
            .Where(p => exclude is null || !exclude.Any(x => p.Contains($"{sep}{x}{sep}", StringComparison.Ordinal)))
            .Select(p => StripComments(File.ReadAllText(p))));
    }

    /// <summary>
    ///     Strips <c>//</c> and <c>/* */</c> comments.
    ///     <para>
    ///         Not cosmetic. Every obligation here is a scan for a written usage, and the corpora are full of
    ///         prose ABOUT the elements — a Gallery header reading "…with [DwarfMapper(GenerateExtensions =
    ///         false)]" and a Conformance section titled "F31 SkipNullSourceMembers" each discharged an
    ///         obligation under the old bare <c>Contains</c> while the option was written nowhere. Comments
    ///         are where an excuse hides after the dictionary holding it is deleted.
    ///     </para>
    ///     <para>
    ///         Deliberately naive about string literals: a <c>//</c> inside one truncates the rest of that
    ///         line. The generator tests embed whole source files in raw string literals, and stripping the
    ///         commentary out of those is wanted, not tolerated — what is being looked for there is a written
    ///         attribute, and no written attribute follows a <c>//</c> on its own line.
    ///     </para>
    /// </summary>
    private static string StripComments(string text)
    {
        var withoutBlocks = Regex.Replace(text, @"/\*.*?\*/", "\n",
            RegexOptions.Singleline, TimeSpan.FromSeconds(30));
        return Regex.Replace(withoutBlocks, @"//[^\n]*", "",
            RegexOptions.None, TimeSpan.FromSeconds(30));
    }
}
