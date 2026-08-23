// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Every emitter must write the SAME null guard, and this scans the generator's source to enforce it.
    ///     <para>
    ///         Round 24 found that it did not. <c>MapEmitter</c> wrote
    ///         <c>ArgumentNullException.ThrowIfNull(source)</c> while <c>MapToGenerator</c> still wrote
    ///         <c>if (source is null) throw new ArgumentNullException(nameof(source))</c> — one product, two
    ///         idioms for one contract. Nothing could see it: both forms compile, so the compiler is silent; a
    ///         snapshot pins whatever text it is handed, so the goldens were green on the wrong idiom; and the
    ///         one test that looked at the guard counted the inline string, so it would have passed on either.
    ///         It surfaced only once the emitted code was fed to the analyzers (CA1510), which is a tool this
    ///         repository had never pointed at its own output.
    ///     </para>
    ///     <para>
    ///         A per-emitter test would not have caught it either — each emitter was self-consistent. The defect
    ///         lived BETWEEN them, so the check has to be over the whole generator at once. That is what makes
    ///         this a source scan rather than another golden file: a THIRD emitter added later is covered on
    ///         arrival, with nobody having to remember.
    ///     </para>
    ///     <para>
    ///         Why the helper form is the right one, per <c>MapEmitter</c>'s own note: the throw lives in a
    ///         <c>[DoesNotReturn]</c> helper, so the hot mapping method stays small IL and inlines readily, and
    ///         an inline <c>throw new …</c> can block that. The paramName is supplied by
    ///         <c>[CallerArgumentExpression]</c>, so the two forms are equivalent to a caller.
    ///     </para>
    /// </summary>
    public class EmittedNullGuardScanTests
    {
        /// <summary>The inline form, written INSIDE a string literal — i.e. emitted, not executed.</summary>
        private static readonly Regex EmittedInlineThrow = new(
            @"""[^""]*throw\s+new\s+global::System\.ArgumentNullException", RegexOptions.Compiled);

        /// <summary>The helper form, likewise emitted.</summary>
        private static readonly Regex EmittedThrowIfNull = new(
            @"""[^""]*global::System\.ArgumentNullException\.ThrowIfNull", RegexOptions.Compiled);

        private static List<string> GeneratorSources()
        {
            var root = Path.Combine(RepoPaths.Root, "src", "DwarfMapper.Generator");
            return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        [Fact]
        public void No_emitter_writes_the_inline_ArgumentNullException_throw()
        {
            var offenders = GeneratorSources()
                .Select(p => (Path: p, Lines: File.ReadAllLines(p)))
                .SelectMany(f => f.Lines.Select((l, i) => (f.Path, No: i + 1, Text: l)))
                .Where(x => EmittedInlineThrow.IsMatch(x.Text))
                .Select(x => $"{Path.GetFileName(x.Path)}:{x.No}")
                .ToList();

            Assert.True(offenders.Count == 0,
                "These emit the INLINE null-guard form while the rest of the generator emits " +
                "ArgumentNullException.ThrowIfNull: " + string.Join(", ", offenders) + ". One product must " +
                "write one idiom for one contract — both compile, so nothing downstream will tell you they " +
                "diverged (round 24: MapToGenerator drifted from MapEmitter and only CA1510 over the EMITTED " +
                "code found it).");
        }

        /// <summary>
        ///     The anti-vacuity half. The scan above passes trivially if the patterns stop matching anything —
        ///     a rename, a refactor to a shared writer, or a wrong repo root would all make it green while
        ///     measuring nothing. This repository has already produced eight mechanisms that looked green and
        ///     measured nothing, so assert the corpus is real and the surviving idiom is actually present.
        /// </summary>
        [Fact]
        public void The_scan_reads_a_real_corpus_and_the_helper_form_is_the_one_in_use()
        {
            var sources = GeneratorSources();
            Assert.True(sources.Count > 20,
                $"only {sources.Count} generator sources found — the scan is looking at the wrong place, so " +
                "its green proves nothing.");

            var emittingFiles = sources
                .Where(p => EmittedThrowIfNull.IsMatch(File.ReadAllText(p)))
                .Select(Path.GetFileName)
                .ToList();

            Assert.True(emittingFiles.Count >= 2,
                "fewer than two generator sources emit ArgumentNullException.ThrowIfNull (found: " +
                string.Join(", ", emittingFiles) + "). The ban above is only meaningful while MORE THAN ONE " +
                "emitter writes a null guard — that plurality is the whole reason the two can drift.");
        }
    }
}
