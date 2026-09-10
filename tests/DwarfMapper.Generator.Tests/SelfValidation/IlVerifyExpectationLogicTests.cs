// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Proves the ILVerify stage's excuse list is checked in BOTH directions.
    ///     <para>
    ///         The stage has always failed on a finding no entry covers. It never failed on the mirror case —
    ///         <b>an entry no finding matches</b> — so a construct could be deleted and its allowance stay
    ///         behind, silently re-permitting whatever next matched that method name. That is the same shape
    ///         <c>DocFenceScanTests</c>' unconverted list and <c>DocsTeachLiveApiTests</c>' foreign list are
    ///         already guarded against, and the one this stage was missing until 2026-09-09.
    ///     </para>
    ///     <para>
    ///         Both halves live in <c>scripts/gate-checks.ps1</c> so they can be driven here against fake
    ///         findings, for the reason <see cref="GateBandLogicTests" /> states: the gates are PowerShell,
    ///         so a C# re-implementation would prove a copy rather than the gate. One <c>pwsh</c> spawn,
    ///         via <see cref="PwshBattery" />.
    ///     </para>
    /// </summary>
    public class IlVerifyExpectationLogicTests
    {
        [Fact]
        public void An_uncovered_finding_fails_and_so_does_an_entry_that_no_finding_matches()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dwarfmapper-ilverify-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                // Fake findings shaped exactly as ilverify prints them, against a two-entry fake list. The
                // real list is not used here: this test is about the LOGIC, and pinning it to the current
                // entries would make it fail whenever an entry legitimately moves.
                File.WriteAllText(Path.Combine(dir, "battery.ps1"),
                    PwshBattery.ScriptPrelude +
                    """
                    $Known = [ordered]@{ 'Fake.A::Run()' = 'a reason'; 'Fake.B::Helper' = 'another reason' }
                    $covered  = @('[IL]: Error [Unverifiable]: [x.dll : Fake.A::Run()][offset 0x03] no.',
                                  '[IL]: Error [ReturnPtrToStack]: [x.dll : Fake.B::Helper(!!0&, int32)][offset 0x0C] no.')
                    $rogue    = $covered + '[IL]: Error [Unverifiable]: [x.dll : Fake.C::Sneaky()][offset 0x01] no.'

                    Show 'all-covered'   { (Assert-IlVerifyFindingsExpected -Target 'x.dll' -ErrorLines $covered -Known $Known) -join ',' }
                    Show 'uncovered'     { Assert-IlVerifyFindingsExpected -Target 'x.dll' -ErrorLines $rogue -Known $Known }
                    Show 'empty-list'    { Assert-IlVerifyFindingsExpected -Target 'x.dll' -ErrorLines $covered -Known ([ordered]@{}) }
                    Show 'no-findings'   { (Assert-IlVerifyFindingsExpected -Target 'x.dll' -ErrorLines @() -Known $Known) -join ',' }
                    Show 'excuses-used'  { Assert-IlVerifyExcusesAllUsed -Known $Known -MatchedKeys @('Fake.A::Run()', 'Fake.B::Helper') }
                    Show 'excuse-stale'  { Assert-IlVerifyExcusesAllUsed -Known $Known -MatchedKeys @('Fake.A::Run()') }
                    Show 'all-stale'     { Assert-IlVerifyExcusesAllUsed -Known $Known -MatchedKeys @() }

                    # A compiler-synthesised name is identical in every assembly, so an entry naming one must
                    # carry the assembly or it excuses that method EVERYWHERE. Same method, two targets: the
                    # Gallery-scoped entry must cover the Gallery finding and refuse the runtime's.
                    $Scoped   = [ordered]@{ 'Gallery.dll : <PrivateImplementationDetails>::Helper' = 'gallery only' }
                    $inGallery = @('[IL]: Error [ReturnPtrToStack]: [/p/Gallery.dll : <PrivateImplementationDetails>::Helper(!!0&)][offset 0x0C] no.')
                    $inRuntime = @('[IL]: Error [ReturnPtrToStack]: [/p/DwarfMapper.dll : <PrivateImplementationDetails>::Helper(!!0&)][offset 0x0C] no.')

                    Show 'scoped-hit'    { (Assert-IlVerifyFindingsExpected -Target 'Gallery.dll' -ErrorLines $inGallery -Known $Scoped) -join ',' }
                    Show 'scoped-miss'   { Assert-IlVerifyFindingsExpected -Target 'DwarfMapper.dll' -ErrorLines $inRuntime -Known $Scoped }
                    """);

                var cases = PwshBattery.Run(dir, 9, "the ILVerify expectation logic");

                // Every finding covered: passes, and reports WHICH entries were used — the input the
                // staleness half needs, so a check returning nothing would silently make it vacuous.
                Assert.Equal("OK Fake.A::Run(),Fake.B::Helper", cases["all-covered"]);

                // One finding no entry covers: fails, naming that finding and not the covered ones.
                Assert.StartsWith("THREW", cases["uncovered"], StringComparison.Ordinal);
                Assert.Contains("Fake.C::Sneaky()", cases["uncovered"], StringComparison.Ordinal);
                Assert.DoesNotContain("Fake.A::Run()", cases["uncovered"], StringComparison.Ordinal);

                // An EMPTY list must not act as a blanket allowance. The previous implementation joined the
                // entries into one regex and needed an explicit '(?!)' sentinel for this case, because an
                // empty pattern matches every line; it was correct, and the sentinel says why. Matching per
                // entry removes the need for a sentinel — this case pins that the property survived the
                // change rather than depending on a guard that is now gone.
                Assert.StartsWith("THREW", cases["empty-list"], StringComparison.Ordinal);
                Assert.Contains("Fake.A::Run()", cases["empty-list"], StringComparison.Ordinal);

                // No findings at all: nothing is unexpected, and nothing was matched either.
                Assert.Equal("OK ", cases["no-findings"]);

                // THE HALF THAT WAS MISSING. Every entry exercised: passes. One entry that no finding
                // matched: fails, naming it. All of them stale: fails naming all of them.
                Assert.Equal("OK PASS", cases["excuses-used"]);
                Assert.StartsWith("THREW", cases["excuse-stale"], StringComparison.Ordinal);
                Assert.Contains("Fake.B::Helper", cases["excuse-stale"], StringComparison.Ordinal);
                Assert.DoesNotContain("Fake.A::Run()", cases["excuse-stale"], StringComparison.Ordinal);
                Assert.Contains("Fake.A::Run()", cases["all-stale"], StringComparison.Ordinal);
                Assert.Contains("Fake.B::Helper", cases["all-stale"], StringComparison.Ordinal);

                // A target-qualified entry covers the assembly it names and NOT another. Without the
                // qualification the same <PrivateImplementationDetails> helper would be excused in
                // src/DwarfMapper.dll, whose clean verification is what this stage most exists to protect.
                Assert.StartsWith("OK Gallery.dll", cases["scoped-hit"], StringComparison.Ordinal);
                Assert.StartsWith("THREW", cases["scoped-miss"], StringComparison.Ordinal);
                Assert.Contains("DwarfMapper.dll", cases["scoped-miss"], StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
