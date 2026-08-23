// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

namespace DwarfMapper.NegativeCases
{
    /// <summary>
    ///     One case file: a deliberately red source shape plus the refusal it must provoke, declared in its own
    ///     header so the expectation lives next to the code rather than in a table somewhere else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The header grammar, all lines being <c>//</c> comments before the first line of real code:
    ///     </para>
    ///     <code>
    ///     // CASE: one-line title
    ///     // WHY:  why this shape is red, in the reader's terms
    ///     // EXPECT: DWARF007, DWARF078          (or `none` for a case that must stay clean)
    ///     // EXPECT-MESSAGE DWARF078: CS8795     (repeatable; substring of the RENDERED message)
    ///     // EXPECT-CS: CS8795                   (optional; compiler errors the emission must produce)
    ///     </code>
    ///     <para>
    ///         <c>EXPECT</c> is an EXACT set, not a "contains". A case that provokes an extra diagnostic is a case
    ///         that changed meaning, and the whole reason this project exists is that such a change was invisible.
    ///         It also keeps cases minimal: the shortest source that produces exactly this set.
    ///     </para>
    /// </remarks>
    internal sealed record NegativeCase(
        string Name,
        string Title,
        string Why,
        ImmutableArray<string> ExpectedIds,
        ImmutableArray<(string Id, string Substring)> ExpectedMessages,
        ImmutableArray<string> ExpectedCompilerErrors,
        string Source)
    {
        private const string Prefix = "DwarfMapper.NegativeCases.Cases.";

        /// <summary>Every case file, loaded from the embedded resources, ordered by name.</summary>
        public static ImmutableArray<NegativeCase> All { get; } = Load();

        /// <summary>The ids this project has a case for — the input to the coverage ratchet.</summary>
        public static ImmutableArray<string> CoveredIds { get; } =
            All.SelectMany(c => c.ExpectedIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToImmutableArray();

        /// <summary>
        ///     The ids some case pins the WORDING of, not merely the presence of — the input to the remedy ratchet.
        /// </summary>
        /// <remarks>
        ///     A subset of <see cref="CoveredIds" /> by construction, and the gap between the two is what
        ///     <c>DiagnosticCoverageRatchetTests.Every_covered_diagnostic_pins_its_remedy_wording</c> refuses to
        ///     let open: <c>A_case_gets_the_message_it_declares</c> returns early for a case with no
        ///     <c>EXPECT-MESSAGE</c> line, so an id-only case satisfies the coverage ratchet while asserting
        ///     nothing about the half of the diagnostic that tells the reader what to write instead.
        /// </remarks>
        public static ImmutableArray<string> WordingPinnedIds { get; } =
            All.SelectMany(c => c.ExpectedMessages.Select(m => m.Id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToImmutableArray();

        public override string ToString()
        {
            return Name;
        }

        private static ImmutableArray<NegativeCase> Load()
        {
            var assembly = typeof(NegativeCase).Assembly;

            var cases = assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".cs", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal)
                .Select(n => Parse(n[Prefix.Length..^".cs".Length], Read(assembly, n)))
                .ToImmutableArray();

            // An empty case set would make every per-case test vacuously pass and the suite green — the exact
            // shape of "skipped and passed look identical" the conformance gate was written to refuse.
            if (cases.Length == 0)
            {
                throw new InvalidOperationException(
                    "No case files were embedded. Check that Cases\\**\\*.cs is an EmbeddedResource " + "(and removed from Compile) in DwarfMapper.NegativeCases.csproj.");
            }

            return cases;
        }

        private static string Read(Assembly assembly, string resourceName)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"Cannot open resource '{resourceName}'.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static NegativeCase Parse(string name, string text)
        {
            string? title = null, why = null;
            var ids = ImmutableArray.CreateBuilder<string>();
            var messages = ImmutableArray.CreateBuilder<(string, string)>();
            var compilerErrors = ImmutableArray.CreateBuilder<string>();
            var sawExpect = false;

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r').Trim();

                // The header is the comment block at the top; the first line of real code ends it. Blank lines
                // and the SPDX line are allowed through so a case can look like an ordinary source file.
                if (line.Length == 0)
                {
                    continue;
                }

                if (!line.StartsWith("//", StringComparison.Ordinal))
                {
                    break;
                }

                var body = line[2..].Trim();

                if (TryTake(body, "CASE:", out var t))
                {
                    title = t;
                }
                else if (TryTake(body, "WHY:", out var w))
                {
                    why = w;
                }
                else if (TryTake(body, "EXPECT-CS:", out var cs))
                {
                    compilerErrors.AddRange(SplitIds(cs));
                }
                else if (TryTake(body, "EXPECT-MESSAGE ", out var m))
                {
                    messages.Add(ParseMessage(name, m));
                }
                else if (TryTake(body, "EXPECT:", out var e))
                {
                    sawExpect = true;
                    if (!string.Equals(e.Trim(), "none", StringComparison.OrdinalIgnoreCase))
                    {
                        ids.AddRange(SplitIds(e));
                    }
                }
            }

            if (!sawExpect)
            {
                throw new InvalidOperationException(
                    $"Case '{name}' has no `// EXPECT:` line. Every case must declare the exact set of DWARF ids " + "it provokes (or `none`) — an undeclared case asserts nothing.");
            }

            if (string.IsNullOrWhiteSpace(why))
            {
                throw new InvalidOperationException(
                    $"Case '{name}' has no `// WHY:` line. State why this shape is red in the reader's terms; a " + "case nobody can explain is a case nobody can maintain.");
            }

            var declared = ids.ToImmutable();

            foreach (var (id, _) in messages)
                if (!declared.Contains(id, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Case '{name}' asserts message text for '{id}', which is not in its EXPECT set.");
                }

            return new NegativeCase(
                name,
                title ?? name,
                why.Trim(),
                declared,
                messages.ToImmutable(),
                compilerErrors.ToImmutable(),
                text);
        }

        private static (string Id, string Substring) ParseMessage(string caseName, string rest)
        {
            var colon = rest.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                throw new InvalidOperationException(
                    $"Case '{caseName}': malformed EXPECT-MESSAGE. Expected `EXPECT-MESSAGE DWARFnnn: substring`.");
            }

            var substring = rest[(colon + 1)..].Trim();
            if (substring.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Case '{caseName}': EXPECT-MESSAGE with an empty substring asserts nothing.");
            }

            return (rest[..colon].Trim(), substring);
        }

        private static bool TryTake(string body, string keyword, out string rest)
        {
            if (body.StartsWith(keyword, StringComparison.Ordinal))
            {
                rest = body[keyword.Length..].Trim();
                return true;
            }

            rest = string.Empty;
            return false;
        }

        private static IEnumerable<string> SplitIds(string value)
        {
            return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToUpperInvariant());
        }

        public string Describe()
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{Name}: {Title}\n  why: {Why}");
        }
    }
}
