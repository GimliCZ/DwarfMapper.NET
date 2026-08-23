// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // Namespace scope, same as RegistryInterfaceLookupTests: CA1034 is an error here, and a NESTED private type
// would take the wrong FormatMessage branch below (IsNestedPrivate is the iterator heuristic's second leg).
    public sealed class ExcPlainSrc
    {
        public int X { get; set; }
    }

    public sealed class ExcPlainDst
    {
        public int X { get; set; }
    }

    /// <summary>
    ///     Pins the OBSERVABLE surface of the two runtime exception types: the message a consumer actually reads
    ///     and the typed properties a handler actually branches on. Round-18/20 mutation runs proved every one of
    ///     these was unpinned — the depth ctor's body could be emptied and each message literal blanked with no
    ///     test noticing (<c>Issues/ledgers/E3-E1-report.md</c>, holes 11–12) — because the runtime tests assert
    ///     only the exception TYPE at the throw sites. The remedy text is load-bearing product surface: it is the
    ///     only thing a user sees at 2 a.m. when a map is missing, and each branch exists to give advice that is
    ///     actionable for that specific failure.
    /// </summary>
    public sealed class DwarfExceptionContractTests
    {
        // ── DwarfMappingDepthException ──────────────────────────────────────────────────────────────────

        /// <summary>
        ///     Both depths must survive into the typed properties AND the message. 64/65 are chosen so neither
        ///     digit string can collide with the message's own fixed figures (the 1000 hard cap).
        /// </summary>
        [Fact]
        public void Depth_exception_carries_both_depths_and_the_remedies()
        {
            var ex = new DwarfMappingDepthException(64, 65);

            Assert.Equal(64, ex.MaxDepth);
            Assert.Equal(65, ex.ActualDepth);

            Assert.Contains("mapping depth exceeded the limit of 64", ex.Message, StringComparison.Ordinal);
            Assert.Contains("Actual depth reached: 65", ex.Message, StringComparison.Ordinal);
            // The two remedies and the hard cap: what makes the message actionable rather than merely loud.
            Assert.Contains("[DwarfMapper(MaxDepth = N)] (max 1000)", ex.Message, StringComparison.Ordinal);
            Assert.Contains("ReferenceHandling = Preserve", ex.Message, StringComparison.Ordinal);
            Assert.Contains("without recursion", ex.Message, StringComparison.Ordinal);
        }

        // ── DwarfMapMissingException.FormatMessage, branch by branch ────────────────────────────────────

        /// <summary>
        ///     The ordinary branch for a plain, attribute-nameable type: the remedy must be the declaration
        ///     advice, NOT the iterator advice. This is the case that discriminates the iterator heuristic —
        ///     a name without <c>'&lt;'</c> on a non-nested type must not be told to <c>.ToList()</c>.
        /// </summary>
        [Fact]
        public void A_plain_type_is_told_to_declare_the_pair()
        {
            var ex = new DwarfMapMissingException(typeof(ExcPlainSrc), typeof(ExcPlainDst));

            Assert.Equal(typeof(ExcPlainSrc), ex.SourceType);
            Assert.Equal(typeof(ExcPlainDst), ex.DestinationType);
            Assert.Empty(ex.AmbiguousInterfaces);

            Assert.Contains("No DwarfMapper map is registered for", ex.Message, StringComparison.Ordinal);
            Assert.Contains("Declare [GenerateMap<ExcPlainSrc, ExcPlainDst>]", ex.Message, StringComparison.Ordinal);
            Assert.Contains("module initializer self-registers", ex.Message, StringComparison.Ordinal);
            // The remedy's SECOND option through its final word: injecting the declaring assembly's concrete
            // mapper is the alternative for a caller who cannot add the attribute, and "directly." is the word
            // that distinguishes it from injecting the facade. (E3-E1 hole 11 remainder: this tail literal
            // could be blanked with no test noticing.)
            Assert.Contains("or inject that assembly's concrete mapper directly.", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("LINQ iterator", ex.Message, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The ambiguous branch must NAME the competing interfaces — reporting rather than picking one is the
        ///     whole design (interface order is undefined), and the names are what lets the reader resolve it.
        /// </summary>
        [Fact]
        public void The_ambiguous_branch_names_every_candidate_interface()
        {
            var ex = new DwarfMapMissingException(typeof(ExcPlainSrc),
                typeof(ExcPlainDst),
                [typeof(IEnumerable<int>), typeof(IReadOnlyCollection<int>)]);

            Assert.Equal(2, ex.AmbiguousInterfaces.Count);

            Assert.Contains("Ambiguous DwarfMapper map for", ex.Message, StringComparison.Ordinal);
            Assert.Contains("more than one of its interfaces has a", ex.Message, StringComparison.Ordinal);
            Assert.Contains("IEnumerable`1, IReadOnlyCollection`1", ex.Message, StringComparison.Ordinal);
            Assert.Contains("Interface order is not defined", ex.Message, StringComparison.Ordinal);
            Assert.Contains("[GenerateMap<ExcPlainSrc, ExcPlainDst>]", ex.Message, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The ambiguous branch's BOUNDARY: exactly ONE candidate interface is not ambiguous, so the guard is
        ///     <c>Count: &gt; 1</c> and a one-element list must fall through to the ordinary "no map is registered"
        ///     message. The distinction is user-facing, not internal bookkeeping: with the guard widened to
        ///     <c>&gt;= 1</c> a consumer holding a single-interface list reads "more than one of its interfaces has
        ///     a registration (IEnumerable`1)" — a sentence that names one interface while asserting there are
        ///     several, and whose remedy (resolve the ambiguity) is unactionable because there is nothing to
        ///     disambiguate. The registry's own <c>Map</c> never builds this list (one accepting interface resolves
        ///     before the throw), but the three-argument constructor is PUBLIC and shipped, so the message it
        ///     produces is contract.
        ///     <para>
        ///         This overturns the round-22 P2 disposition that graded the boundary "probably-equivalent, do not
        ///         chase" (<c>Issues/ledgers/equivalent-mutants.md</c>, runtime / FormatMessage ambiguous-branch
        ///         guard). That grade's own reasoning conceded the distinguishing call "IS expressible"; the
        ///         category is defined as low-priority, never do-not-attempt. Killing it moves the runtime leg's
        ///         <c>probablyEquivalent</c> count 1 → 0 — a ledger edit for the maintainer, not for this test.
        ///     </para>
        /// </summary>
        [Fact]
        public void One_candidate_interface_is_not_ambiguous_and_gets_the_ordinary_message()
        {
            var ex = new DwarfMapMissingException(typeof(ExcPlainSrc),
                typeof(ExcPlainDst),
                [typeof(IEnumerable<int>)]);

            Assert.Single(ex.AmbiguousInterfaces);
            Assert.Equal(typeof(IEnumerable<int>), ex.AmbiguousInterfaces[0]);

            // The fall-through branch, word for word: a single candidate is reported as "nothing matched", and the
            // remedy is the declaration advice rather than the disambiguation advice.
            Assert.Contains("No DwarfMapper map is registered for", ex.Message, StringComparison.Ordinal);
            Assert.Contains("Lookup tried the runtime type, its base types, and its interfaces.",
                ex.Message,
                StringComparison.Ordinal);
            Assert.Contains("Declare [GenerateMap<ExcPlainSrc, ExcPlainDst>]", ex.Message, StringComparison.Ordinal);

            // And none of the ambiguous branch's vocabulary may appear — each of these is a distinct half of the
            // wrong message, so the assertion survives a partial rewrite of either branch.
            Assert.DoesNotContain("Ambiguous DwarfMapper map for", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("more than one of its interfaces", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Interface order is not defined", ex.Message, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The update branch must say UPDATE-INTO and that a create map does not satisfy it — the two tables
        ///     are keyed separately, and without that sentence the reader hunts for a registration that already
        ///     exists. The suggested declaration must be the two-parameter partial method, not [GenerateMap].
        /// </summary>
        [Fact]
        public void The_update_branch_says_which_key_space_is_empty()
        {
            var ex = new DwarfMapMissingException(typeof(ExcPlainSrc), typeof(ExcPlainDst), null, true);

            Assert.Contains("UPDATE-INTO", ex.Message, StringComparison.Ordinal);
            Assert.Contains("A create-map for the same pair does not satisfy this", ex.Message, StringComparison.Ordinal);
            Assert.Contains("keyed separately", ex.Message, StringComparison.Ordinal);
            Assert.Contains("public partial void Update(ExcPlainSrc source, ExcPlainDst destination);",
                ex.Message,
                StringComparison.Ordinal);
        }
    }
}
