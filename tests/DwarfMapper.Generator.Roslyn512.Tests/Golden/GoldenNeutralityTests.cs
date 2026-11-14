// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;

namespace DwarfMapper.Generator.Roslyn512.Tests.Golden
{
    /// <summary>
    ///     The Roslyn 5.12 build emits, for every case of the golden corpus, exactly what the Roslyn 5.0 build emits.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Round 35 T23. The two builds compile one source tree, and only files named <c>*.Floor.cs</c> (5.0 only) or
    ///         kept in the Roslyn512 project's folder (5.12 only) differ. The corpus is LINKED from Generator.Tests, and
    ///         the expected value of each row is that project's committed manifest line - the fingerprint of every file
    ///         the 5.0 build generated for the case plus every diagnostic with its message. So a row here fails exactly
    ///         when the 5.12 build says something different about a case the 5.0 build was pinned on.
    ///     </para>
    ///     <para>
    ///         Union mapping is not in the corpus: it is C# 15, which the corpus (parsed as C# 14 on both builds) does
    ///         not contain. That is the point - the corpus is everything the 5.12 build must NOT change.
    ///     </para>
    /// </remarks>
    public sealed class GoldenNeutralityTests
    {
        private static readonly Lazy<IReadOnlyDictionary<string, string>> Manifest = new(GoldenManifestFile.Load);

        public static TheoryData<string> CaseIds()
        {
            var data = new TheoryData<string>();
            foreach (var c in GoldenCorpus.Cases())
                data.Add(c.Id);

            return data;
        }

        [Theory]
        [MemberData(nameof(CaseIds))]
        public void Every_golden_case_is_byte_identical_on_the_512_build(string id)
        {
            Assert.True(Manifest.Value.TryGetValue(id, out var pinned),
                $"'{id}' is a corpus case with no manifest line; Generator.Tests' GoldenCorpusTests owns adding it.");
            Assert.Equal(pinned, GoldenFingerprint.Compute(GoldenCorpus.Case(id)));
        }

        [Fact]
        public void The_corpus_and_the_manifest_name_the_same_cases()
        {
            var corpus = GoldenCorpus.Cases().Select(c => c.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
            var manifest = Manifest.Value.Keys.OrderBy(i => i, StringComparer.Ordinal).ToList();

            // The vacuity guard: a corpus that shrank to nothing would agree with an empty manifest.
            Assert.True(corpus.Count >= 1000, $"the golden corpus holds {corpus.Count} cases; it held 1,021 when this test was written");
            Assert.Equal(manifest, corpus);
        }
    }
}
