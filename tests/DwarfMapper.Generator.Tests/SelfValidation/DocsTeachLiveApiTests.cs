// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.DocTooling;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Every API call the user-facing documentation teaches must still exist.
    ///     <para>
    ///         Found by this file on 2026-09-09: <c>README.md</c> told readers twice to call
    ///         <c>ObjectFactory.Create&lt;T&gt;(seed)</c> to rebuild a failing fixture, and
    ///         <c>DwarfMapper.Testing.ObjectFactory</c> had been deleted — merged into <c>ObjectFactoryV2</c> —
    ///         earlier in the same unreleased cycle. A reader following the instruction gets <c>CS0103</c>. The
    ///         snippet pipeline could not catch it because the call sits in PROSE, and prose is the one part of
    ///         the documentation nothing compiles.
    ///     </para>
    ///     <para>
    ///         The check is a closed world over CALL-SHAPED code spans only — <c>`Type.Member(`</c> or
    ///         <c>`Type.Member&lt;`</c>. That restriction is what makes it cheap: a namespace
    ///         (<c>`DwarfMapper.Testing`</c>), a member reference (<c>`Address.City`</c>) and an enum value
    ///         (<c>`Color.Red`</c>) are not calls, so they never enter the population and never need an excuse.
    ///         Fifteen spans qualify across the whole documentation set, and only two lists are needed to
    ///         account for all of them.
    ///     </para>
    /// </summary>
    public class DocsTeachLiveApiTests
    {
        // A code span that is a static-ish call: `Type.Member(` or `Type.Member<`. Both halves PascalCase, which
        // is what separates a call from `order.Lines` and from a namespace segment.
        private static readonly Regex CallSpan = new(
            @"`(?<type>[A-Z][A-Za-z0-9_]*)\.(?<member>[A-Z][A-Za-z0-9_]*)[(<][^`]*`",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        ///     Names the documentation teaches that are NOT ours. Every entry is a type we do not ship and cannot
        ///     move: the BCL (<c>ArgumentNullException</c>, <c>Array</c>, <c>DateTime</c>, <c>Enum</c>,
        ///     <c>Enumerable</c>, <c>Int32</c>, <c>MemoryMarshal</c>) and a competitor's surface quoted in a
        ///     migration guide (<c>IMapper</c>, AutoMapper). A new entry here is a claim that a name is foreign;
        ///     the count is pinned below so making that claim is deliberate.
        /// </summary>
        private static readonly HashSet<string> Foreign = new(StringComparer.Ordinal)
        {
            "ArgumentNullException",
            "Array",
            "DateTime",
            "Enum",
            "Enumerable",
            "IMapper",
            "Int32",
            "MemoryMarshal"
        };

        /// <summary>
        ///     Names the GENERATOR emits into the consumer's assembly, so they legitimately appear in no
        ///     <c>PublicAPI.*.txt</c> — there is no shipped assembly that declares them. Each maps to the emitter
        ///     that writes it, and the test below proves that anchor still emits that exact name. Without the
        ///     anchor check this list would be an allowlist; with it, deleting the emitter turns the docs red.
        /// </summary>
        private static readonly Dictionary<string, string> Generated = new(StringComparer.Ordinal)
        {
            ["DwarfMap"] = "src/DwarfMapper.Generator/Pipeline/AmbientValidator.cs"
        };

        private const int PinnedForeignCount = 8;

        // Fifteen distinct call spans exist as of 2026-09-09. The floor is what stops the regex from being
        // narrowed to nothing: a scanner that matches zero spans would otherwise pass this whole file.
        private const int MinimumCallSpansFound = 15;

        [Fact]
        public void Every_call_the_docs_teach_resolves_to_a_shipped_or_emitted_api()
        {
            var exported = ExportedSurface();
            var offenders = new List<string>();

            foreach (var relative in DocSet.All)
            {
                foreach (Match m in CallSpan.Matches(DocSet.Read(relative)))
                {
                    var type = m.Groups["type"].Value;
                    var member = m.Groups["member"].Value;
                    if (Foreign.Contains(type) || Generated.ContainsKey(type))
                    {
                        continue;
                    }

                    if (!exported.Contains("." + type + "." + member, StringComparison.Ordinal))
                    {
                        offenders.Add($"{relative}: `{type}.{member}(`");
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "The documentation teaches (a) call(s) that no PublicAPI.*.txt declares, so a reader following " +
                "it does not compile. Fix the prose to name the current API, or — if the name is not ours — add " +
                "the type to Foreign with the count pin moved in the same commit:\n  " +
                string.Join("\n  ", offenders.Distinct(StringComparer.Ordinal)));
        }

        [Fact]
        public void Every_generated_name_the_docs_teach_is_still_emitted_by_its_anchor()
        {
            // The half that keeps `Generated` from being an allowlist: the emitter named beside each entry must
            // exist AND still emit a type of that name. Deleting AmbientValidator's DwarfMap emission would
            // otherwise leave `DwarfMap.Validate()` documented forever.
            foreach (var (type, anchor) in Generated)
            {
                var path = Path.Combine(RepoPaths.Root, anchor);
                Assert.True(File.Exists(path),
                    $"docs teach the generated type '{type}', anchored to '{anchor}', which does not exist. An " +
                    "anchor that cannot be found is an allowlist.");

                // The declaration is emitted as a string literal, so the closing quote is part of the anchor.
                // Without it, "class DwarfMap" also matches "class DwarfMapperValidationRoot" and the check
                // stops being about the name it claims to pin.
                Assert.Contains("class " + type + "\"", File.ReadAllText(path), StringComparison.Ordinal);
            }
        }

        [Fact]
        public void The_scanner_finds_the_population_it_claims_to_check()
        {
            // Non-vacuity. Both assertions above pass trivially over an empty population, which is exactly how a
            // tightened regex or a renamed DocSet would silently retire this file.
            var found = DocSet.All
                .SelectMany(d => CallSpan.Matches(DocSet.Read(d)).Cast<Match>())
                .Select(m => m.Groups["type"].Value + "." + m.Groups["member"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.True(found.Count >= MinimumCallSpansFound,
                $"Only {found.Count} call-shaped code span(s) found across DocSet.All, expected at least " +
                $"{MinimumCallSpansFound}. Either the documentation lost that much of its API prose, or the " +
                "scanner stopped matching — and a scanner that matches nothing passes every other test here.");
        }

        [Fact]
        public void The_foreign_list_is_pinned_and_every_entry_is_actually_used()
        {
            Assert.True(Foreign.Count == PinnedForeignCount,
                $"Foreign holds {Foreign.Count} name(s), pinned {PinnedForeignCount}. Declaring a name foreign " +
                "is a claim that we do not ship it; move the pin in the same commit that makes the claim.");

            // An entry left behind after the prose that needed it was deleted silently re-permits that name.
            var referenced = DocSet.All
                .SelectMany(d => CallSpan.Matches(DocSet.Read(d)).Cast<Match>())
                .Select(m => m.Groups["type"].Value)
                .ToHashSet(StringComparer.Ordinal);

            var unused = Foreign.Where(f => !referenced.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
            Assert.True(unused.Count == 0,
                "Foreign entr(ies) no longer named by any document: " + string.Join(", ", unused) +
                ". Delete them — an excuse outliving its case is how an allowlist forms.");
        }

        private static string ExportedSurface()
        {
            var files = Directory.GetFiles(RepoPaths.Src, "PublicAPI.*.txt", SearchOption.AllDirectories);

            Assert.True(files.Length > 0,
                "No PublicAPI.*.txt found under src/ — the closed world this test compares against is empty, " +
                "which would let every documented call pass.");

            return string.Join("\n", files.Select(File.ReadAllText));
        }
    }
}
