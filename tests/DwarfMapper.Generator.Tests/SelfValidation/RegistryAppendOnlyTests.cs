// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     SECURITY INVARIANT [SEC-2]: a registration can never be REPLACED. The ambient registry's tables are
    ///     append-only.
    ///     <para>
    ///         This is the property that makes the global registry defensible. <c>Register</c> is public and
    ///         static and must be — every consumer assembly self-registers from a module initialiser, so any
    ///         loaded assembly can add to the tables. What no assembly can do is take a pair that another
    ///         assembly already claimed: <c>TryAdd</c> never overwrites, a duplicate marks the pair
    ///         <c>Ambiguous</c>, and the first registration stands. There is no overwrite path, so map
    ///         substitution is not merely unlikely but structurally unavailable.
    ///     </para>
    ///     <para>
    ///         The runtime BEHAVIOUR is already covered by <c>RegistryConcurrencyTortureTests</c> and
    ///         <c>AmbientRegistryTests</c>. What was missing, and is what this file adds, is the STRUCTURAL
    ///         guarantee: nothing stopped a future edit from introducing <c>table[key] = map</c> or
    ///         <c>AddOrUpdate</c>, which would silently convert first-wins into last-wins and invalidate the
    ///         paragraph above without failing a single existing test.
    ///     </para>
    ///     <para>
    ///         The limit of the guarantee, stated because SECURITY.md now states it too: first-wins means
    ///         REGISTRATION ORDER DECIDES, and <c>TryGet</c> does not consult <c>IsAmbiguous</c>. A shadowed
    ///         duplicate therefore resolves silently unless the consumer opts into
    ///         <c>[DwarfMapperValidationRoot]</c>. That is a documented trust boundary, not a bug this test
    ///         pretends away.
    ///     </para>
    /// </summary>
    public class RegistryAppendOnlyTests
    {
        /// <summary>
        ///     Mutating operations that would break first-wins. <c>TryAdd</c> and <c>Add</c> are the sanctioned
        ///     ones and are deliberately absent from this list.
        /// </summary>
        private static readonly string[] ForbiddenMutations =
        [
            "AddOrUpdate", "TryRemove", ".Remove(", ".Clear()", "TryUpdate", "GetOrAdd"
        ];

        private static string RegistrySource()
        {
            var path = Path.Combine(RepoPaths.Src, "DwarfMapper", "DwarfMapperRegistry.cs");
            Assert.True(File.Exists(path), $"DwarfMapperRegistry.cs not found at {path} — this scan reads nothing.");

            var src = File.ReadAllText(path);
            src = Regex.Replace(src, @"//[^\n]*", string.Empty);
            src = Regex.Replace(src, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            return src;
        }

        /// <summary>The static tables the invariant is about, discovered rather than hard-coded.</summary>
        private static List<string> TableNames()
        {
            // Anchored on the ASSIGNMENT, not on the type. A first version matched the generic argument list
            // with <[^>]*>, which cannot see through a nested generic: `ConcurrentDictionary<Key,
            // Func<object, object>> Maps` stopped at the inner `>` and yielded "Map", while the two
            // declarations wrapped across lines were missed entirely — three tables found instead of five.
            // The identifier immediately preceding `=` is unambiguous and survives both.
            return Regex.Matches(RegistrySource(),
                    @"private static readonly[\s\S]*?(?<name>\w+)\s*=",
                    RegexOptions.ExplicitCapture)
                .Select(m => m.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        [Fact]
        public void The_scan_finds_every_registry_table()
        {
            // Non-vacuity: if the field shape changes and this finds nothing, the mutation scan below would
            // pass while guarding an empty set.
            var tables = TableNames();

            Assert.True(tables.Count >= 5,
                $"Expected at least the five registry tables, found {tables.Count} ({string.Join(", ", tables)}). "
                + "The field declarations changed shape and this scan no longer describes the registry.");
            Assert.Contains("Maps", tables, StringComparer.Ordinal);
            Assert.Contains("Ambiguous", tables, StringComparer.Ordinal);
        }

        [Fact]
        public void No_registry_table_is_ever_overwritten_or_removed_from()
        {
            var src = RegistrySource();
            var offenders = new List<string>();

            foreach (var op in ForbiddenMutations)
            {
                if (src.Contains(op, StringComparison.Ordinal))
                {
                    offenders.Add(op);
                }
            }

            // Indexer assignment: `Maps[key] = value`. TryAdd is the only sanctioned insert.
            foreach (var table in TableNames())
            {
                if (Regex.IsMatch(src, Regex.Escape(table) + @"\s*\[[^\]]+\]\s*="))
                {
                    offenders.Add($"{table}[...] = ...");
                }
            }

            Assert.True(offenders.Count == 0,
                "The ambient registry acquired a mutation that can REPLACE or DROP a registration. First-wins is "
                + "what makes a public, globally-callable Register defensible: no loaded assembly can take a pair "
                + "another already claimed. This change would convert that into last-wins, silently, without "
                + "failing any behavioural test. SECURITY.md documents the current guarantee and must be "
                + "rewritten before this is relaxed:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Every_insert_goes_through_TryAdd_or_bag_Add()
        {
            // The positive half: the sanctioned operations must actually be the ones in use, or the rule above
            // is guarding a mechanism that no longer exists.
            var src = RegistrySource();

            Assert.True(Regex.Matches(src, @"\.TryAdd\(").Count >= 4,
                "Fewer than four TryAdd calls remain in the registry — the create and update tables plus their "
                + "ambiguity mirrors each need one. Inserts have moved to some other mechanism, and the "
                + "append-only scan above is now checking the wrong thing.");
        }

        [Fact]
        public void The_mutation_scan_would_actually_catch_a_reintroduced_overwrite()
        {
            // Control. A scan that cannot fail proves nothing.
            const string planted = "Maps[key] = map; Ambiguous.AddOrUpdate(key, 1, (_, v) => v);";

            Assert.Contains(ForbiddenMutations, op => planted.Contains(op, StringComparison.Ordinal));
            Assert.Matches(@"Maps\s*\[[^\]]+\]\s*=", planted);

            // And it must not fire on the sanctioned form, or it would be unusable.
            Assert.DoesNotMatch(@"Maps\s*\[[^\]]+\]\s*=", "if (!Maps.TryAdd(key, map)) { }");
        }
    }
}
