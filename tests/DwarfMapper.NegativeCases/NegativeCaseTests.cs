// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.NegativeCases
{
    /// <summary>
    ///     Runs every case file and holds the generator to the refusal the file declares.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The companion to <c>tests/DwarfMapper.ConsumerTests</c>: that project asserts what a consumer's
    ///         program DOES at run time across assembly boundaries, this one asserts what the build REFUSES to
    ///         produce. Between them they cover the two things a snapshot cannot — behaviour and rejection.
    ///     </para>
    ///     <para>
    ///         Message text is asserted, not only the id, because Round 18 produced three separate tasks whose
    ///         entire content was "the id was right and the message did not help". A diagnostic's id tells the
    ///         reader which rule they hit; its message is the only part that tells them what to write instead, and
    ///         it is the part with no other test.
    ///     </para>
    /// </remarks>
    public class NegativeCaseTests
    {
        public static TheoryData<string> CaseNames()
        {
            var data = new TheoryData<string>();
            foreach (var c in NegativeCase.All) data.Add(c.Name);
            return data;
        }

        private static NegativeCase Case(string name)
        {
            return NegativeCase.All.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void A_case_provokes_exactly_the_diagnostics_it_declares(string name)
        {
            var testCase = Case(name);
            var outcome = CaseDriver.Run(testCase.Source, "NegCase_" + name);
            var actual = CaseDriver.DwarfIds(outcome.Generator);

            var missing = testCase.ExpectedIds.Except(actual, StringComparer.Ordinal).ToList();
            var unexpected = actual.Except(testCase.ExpectedIds, StringComparer.Ordinal).ToList();

            Assert.True(missing.Count == 0 && unexpected.Count == 0,
                $"{testCase.Describe()}\n\n" +
                $"  declared: {Join(testCase.ExpectedIds)}\n" +
                $"  actual:   {Join(actual)}\n" +
                (missing.Count > 0 ? $"  NOT REPORTED: {Join(missing)}\n" : string.Empty) +
                (unexpected.Count > 0 ? $"  ALSO REPORTED: {Join(unexpected)}\n" : string.Empty) +
                "\nThe declared set is exact on purpose. If the new id is correct, add it to the case header; if " +
                "it is a regression, the case has just done its job.\n\n" +
                Render(outcome.Generator));
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void A_case_gets_the_message_it_declares(string name)
        {
            var testCase = Case(name);
            if (testCase.ExpectedMessages.Length == 0)
            {
                return;
            }

            var outcome = CaseDriver.Run(testCase.Source, "NegMsg_" + name);

            foreach (var (id, substring) in testCase.ExpectedMessages)
            {
                var rendered = outcome.Generator
                    .Where(d => string.Equals(d.Id, id, StringComparison.Ordinal))
                    .Select(d => d.GetMessage(CultureInfo.InvariantCulture))
                    .ToList();

                Assert.True(rendered.Count > 0, $"{testCase.Describe()}\n\n  '{id}' was not reported at all.");

                Assert.True(rendered.Exists(m => m.Contains(substring, StringComparison.Ordinal)),
                    $"{testCase.Describe()}\n\n" + $"  {id}'s message no longer contains \"{substring}\".\n\n" + "  what it said instead:\n    " + string.Join("\n    ", rendered) + "\n\nThis is the half of the diagnostic that tells the reader what to write instead. " + "Reword freely; keep the way out.");
            }
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void A_case_produces_the_compiler_errors_it_declares(string name)
        {
            var testCase = Case(name);
            if (testCase.ExpectedCompilerErrors.Length == 0)
            {
                return;
            }

            var outcome = CaseDriver.Run(testCase.Source, "NegCs_" + name);

            var actual = outcome.Compilation
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Id)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var expected in testCase.ExpectedCompilerErrors)
                Assert.True(actual.Contains(expected, StringComparer.Ordinal),
                    $"{testCase.Describe()}\n\n" +
                    $"  expected the emission to produce {expected}, but the compiler reported: " +
                    $"{(actual.Count == 0 ? "(nothing)" : string.Join(", ", actual))}\n\n" +
                    "This assertion exists because a DwarfMapper diagnostic that EXPLAINS a compiler error is " +
                    "only correct while that compiler error is still what the consumer sees. If the cascade " +
                    "changed shape, the explanation is now misinformation.");
        }

        private static string Join(IEnumerable<string> ids)
        {
            var list = ids.ToList();
            return list.Count == 0 ? "(none)" : string.Join(", ", list);
        }

        private static string Render(IEnumerable<Diagnostic> diagnostics)
        {
            var list = diagnostics
                .Select(d => $"  [{d.Severity}] {d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}")
                .ToList();

            return list.Count == 0
                ? "--- the generators reported nothing at all ---"
                : "--- everything the generators reported ---\n" + string.Join("\n", list);
        }
    }
}
