// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     SECURITY INVARIANT [SEC-4]: <c>AllowNonPublic</c> widens what the generator will bind to strictly
    ///     WITHIN the C# accessibility rules. It is not a reflective back door.
    ///     <para>
    ///         The BEHAVIOUR is already well covered — <c>ConstructorSelectorHardeningTests</c> pins that an
    ///         <c>internal</c> constructor is bound with the flag and a <c>private</c> or <c>protected</c> one is
    ///         still refused (<c>DWARF026</c>) — so another behavioural test would be noise. What was missing is
    ///         the STRUCTURAL guarantee, and it is the one that could regress silently: nothing stopped a future
    ///         edit from deciding accessibility with a hand-rolled comparison instead of asking Roslyn.
    ///     </para>
    ///     <para>
    ///         The rule distinguishes DECIDING from DELEGATING. A method that merely threads the flag onward as
    ///         an argument makes no accessibility judgement and needs no API call. A method that reads the flag
    ///         in a condition IS the decision, and must consult a sanctioned API in the same method — otherwise
    ///         the widening is happening somewhere nobody is checking the C# rules.
    ///     </para>
    ///     <para>
    ///         Two sanctioned APIs, not one. <c>IsSymbolAccessibleWithin</c> is the general answer;
    ///         <c>GivesAccessTo</c> is the <c>[InternalsVisibleTo]</c> query that <c>MemberFacts</c> uses
    ///         deliberately, because — as its own comment records — <c>IsSymbolAccessibleWithin</c> is
    ///         unreliable for property accessors scoped to an <c>IAssemblySymbol</c>. Both honour the language
    ///         rules; a hand-rolled <c>DeclaredAccessibility</c> comparison in a widening position would not.
    ///     </para>
    ///     <para>
    ///         Note that a check REQUIRING public can never widen access, which is why the many
    ///         <c>DeclaredAccessibility == Public</c> narrowing tests across the generator are irrelevant here
    ///         and deliberately not flagged.
    ///     </para>
    /// </summary>
    public class AccessibilityBoundaryTests
    {
        /// <summary>The APIs that answer "may this assembly legally see this symbol?" per the language rules.</summary>
        private static readonly string[] SanctionedApis = ["IsSymbolAccessibleWithin", "GivesAccessTo"];

        /// <summary>
        ///     The centralized member-lookup helpers. A caller that routes through one of these has delegated
        ///     the accessibility judgement rather than made it: <c>MemberFacts</c> performs the assembly-identity
        ///     and <c>[InternalsVisibleTo]</c> check in one place, and every caller inherits it.
        ///     <para>
        ///         This allowance is not a loophole, because the helpers are themselves decision sites and this
        ///         same rule applies to them — <c>MemberFacts</c> passes by calling <c>GivesAccessTo</c>. The
        ///         centralization IS the security property: one implementation to audit instead of one per
        ///         caller, which is exactly what the ISSUE-044 comment in that file argues for.
        ///     </para>
        /// </summary>
        private static readonly string[] SanctionedRouters = ["ReadableMembers", "WritableMembers", "MemberFacts."];

        /// <summary>
        ///     Methods that read the flag in a condition but legitimately make no accessibility judgement,
        ///     each with its reason. SHRINK-ONLY, and empty is the goal — a row here is a claim that a
        ///     decision-shaped use is not really a decision, which deserves to be read.
        /// </summary>
        private static readonly Dictionary<string, string> Delegators = new(StringComparer.Ordinal)
        {
            // Populated from measurement in the commit that adds this file.
        };

        private static bool MentionsFlag(SyntaxNode node, out bool decides)
        {
            decides = false;
            var found = false;

            foreach (var id in node.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (!id.Identifier.ValueText.StartsWith("allowNonPublic", StringComparison.Ordinal)) { continue; }

                found = true;

                // Passing it on is delegation; anything else — a condition, a negation, a ternary test — is
                // the decision itself.
                //
                // NameColonSyntax is delegation too, and missing that was a real false positive: in
                // `Helper(allowNonPublic: flag)` the LABEL parses as an IdentifierNameSyntax whose parent is
                // the NameColon rather than the Argument, so a method that only ever threaded the flag by
                // name looked like it was deciding on it. That flagged ExtractCore, whose sole non-argument
                // mention is `var allowNonPublic = ReadAllowNonPublic(opts);`.
                if (id.Parent is not ArgumentSyntax && id.Parent is not NameColonSyntax)
                {
                    decides = true;
                }
            }

            return found;
        }

        private static List<(string Key, bool Sanctioned)> DecisionSites()
        {
            var sites = new List<(string, bool)>();

            foreach (var path in RepoPaths.SourceFiles(RepoPaths.GeneratorSrcDir))
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
                var file = Path.GetFileName(path);

                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (!MentionsFlag(method, out var decides) || !decides) { continue; }

                    var text = method.ToString();
                    var sanctioned = SanctionedApis.Any(api => text.Contains(api, StringComparison.Ordinal))
                                     || SanctionedRouters.Any(r => text.Contains(r, StringComparison.Ordinal));
                    sites.Add(($"{file}::{method.Identifier.ValueText}", sanctioned));
                }
            }

            return sites;
        }

        [Fact]
        public void The_scan_finds_the_accessibility_decision_sites()
        {
            // Non-vacuity. If the flag were renamed and this found nothing, the rule below would pass while
            // guarding an empty set — the failure mode this repository keeps finding in its own instruments.
            var sites = DecisionSites();

            Assert.True(sites.Count >= 2,
                $"Only {sites.Count} accessibility decision sites found. The `allowNonPublic` parameter was "
                + "renamed, or the generator layout moved, and this scan no longer describes the code.");
        }

        [Fact]
        public void Every_accessibility_decision_consults_a_sanctioned_API()
        {
            var offenders = DecisionSites()
                .Where(s => !s.Sanctioned && !Delegators.ContainsKey(s.Key))
                .Select(s => s.Key)
                .ToList();

            Assert.True(offenders.Count == 0,
                "A method decides on `allowNonPublic` without asking Roslyn whether the symbol is legally "
                + "reachable. AllowNonPublic widens binding WITHIN the C# rules — it is not a reflective back "
                + "door, and SECURITY.md says so. Use IsSymbolAccessibleWithin (or GivesAccessTo for the "
                + "InternalsVisibleTo query), or add a Delegators row explaining why this use decides "
                + "nothing:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void The_delegator_allowlist_carries_no_stale_rows()
        {
            var live = DecisionSites().Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

            var stale = Delegators.Keys
                .Where(k => !live.Contains(k))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            Assert.True(stale.Count == 0,
                "A Delegators row no longer matches any method — it was renamed or fixed. Delete it; an "
                + "allowlist that outlives its subject overstates the debt while still passing:\n  "
                + string.Join("\n  ", stale));
        }

        [Fact]
        public void The_scan_distinguishes_deciding_from_delegating()
        {
            // Control, in both directions. Getting this backwards would either flag every threading site or
            // flag none — and "flag none" is the dangerous direction.
            var deciding = CSharpSyntaxTree.ParseText(
                    "class C { bool M(bool allowNonPublic) { return allowNonPublic && Q(); } }")
                .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();

            var delegating = CSharpSyntaxTree.ParseText(
                    "class C { bool M(bool allowNonPublic) { return Helper(x, allowNonPublic); } }")
                .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();

            var named = CSharpSyntaxTree.ParseText(
                    "class C { bool M(bool allowNonPublic) { return Helper(x, allowNonPublic: allowNonPublic); } }")
                .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();

            Assert.True(MentionsFlag(deciding, out var d1) && d1);
            Assert.True(MentionsFlag(delegating, out var d2) && !d2);

            // The named-argument case, which a first version got wrong in the dangerous direction: it read a
            // threading site as a decision, which would have pushed someone to "fix" code that was correct.
            Assert.True(MentionsFlag(named, out var d3) && !d3);
        }
    }
}
