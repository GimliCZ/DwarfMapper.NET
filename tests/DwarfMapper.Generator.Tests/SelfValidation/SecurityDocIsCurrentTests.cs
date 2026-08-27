// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <c>SECURITY.md</c>'s trust model cites a test for every invariant it claims. This asserts those
    ///     tests exist.
    ///     <para>
    ///         A security document that names a guard which was renamed or deleted is worse than one that names
    ///         none: it reads as evidence while pointing at nothing, and a reader auditing the claim finds an
    ///         empty reference rather than an absent one. The citations are the load-bearing part of that
    ///         section — they are what turns "we do no reflection" from a promise into something checkable — so
    ///         they get the same fail-loud treatment as the generated-docs pins.
    ///     </para>
    ///     <para>
    ///         This checks that a cited guard EXISTS. It cannot check that the guard proves what the prose says
    ///         it proves — that judgement stays with the reader, which is why each cited test carries its
    ///         reasoning in its own summary rather than only in the document.
    ///     </para>
    /// </summary>
    public class SecurityDocIsCurrentTests
    {
        private static string SecurityDoc()
        {
            var path = Path.Combine(RepoPaths.Root, "SECURITY.md");
            Assert.True(File.Exists(path), $"SECURITY.md not found at {path}.");
            return File.ReadAllText(path);
        }

        /// <summary>Every `*Held by …*` citation line, flattened to the identifiers it names.</summary>
        private static (List<string> Classes, List<string> Methods) Citations()
        {
            var classes = new List<string>();
            var methods = new List<string>();

            foreach (Match line in Regex.Matches(SecurityDoc(), @"\*Held by (?<body>[^*]+)\*", RegexOptions.ExplicitCapture))
            {
                var body = line.Groups["body"].Value;

                foreach (Match m in Regex.Matches(body, @"`(?<id>[A-Za-z_][\w]*)`", RegexOptions.ExplicitCapture))
                {
                    var id = m.Groups["id"].Value;
                    (id.EndsWith("Tests", StringComparison.Ordinal) ? classes : methods).Add(id);
                }
            }

            return (classes.Distinct(StringComparer.Ordinal).ToList(), methods.Distinct(StringComparer.Ordinal).ToList());
        }

        private static string AllTestSources()
        {
            var sb = new System.Text.StringBuilder();

            foreach (var path in RepoPaths.SourceFiles(RepoPaths.Tests))
            {
                sb.Append(File.ReadAllText(path)).Append('\n');
            }

            return sb.ToString();
        }

        [Fact]
        public void The_trust_model_section_exists_and_cites_its_guards()
        {
            // Non-vacuity in both directions: the section must be present, and it must actually carry
            // citations. A trust model with no citations would pass every check below by naming nothing.
            var doc = SecurityDoc();

            Assert.Contains("## Trust model", doc, StringComparison.Ordinal);

            var (classes, _) = Citations();
            Assert.True(classes.Count >= 5,
                $"SECURITY.md's trust model cites only {classes.Count} test classes. Every invariant it claims "
                + "must name the guard that holds it, or the section is prose asserting itself.");
        }

        [Fact]
        public void Every_test_class_cited_by_SECURITY_md_exists()
        {
            var (classes, _) = Citations();
            var sources = AllTestSources();

            var missing = classes
                .Where(c => !Regex.IsMatch(sources, @"\bclass\s+" + Regex.Escape(c) + @"\b"))
                .ToList();

            Assert.True(missing.Count == 0,
                "SECURITY.md cites a test class that does not exist. A security document naming a guard that "
                + "was renamed or deleted reads as evidence while pointing at nothing — fix the citation, or "
                + "restore the guard:\n  " + string.Join("\n  ", missing));
        }

        [Fact]
        public void Every_test_method_cited_by_SECURITY_md_exists()
        {
            var (_, methods) = Citations();
            var sources = AllTestSources();

            var missing = methods
                .Where(m => !sources.Contains(m, StringComparison.Ordinal))
                .ToList();

            Assert.True(missing.Count == 0,
                "SECURITY.md cites a test method that does not exist. The named methods are the specific "
                + "assertions a reader would go and read — an unresolvable one is a dead reference in the "
                + "document most likely to be audited:\n  " + string.Join("\n  ", missing));
        }

        [Fact]
        public void The_citation_scan_would_actually_catch_a_dead_reference()
        {
            // Control. The matcher must resolve a real class and reject an invented one, or the two tests
            // above would pass whatever the document said.
            var sources = AllTestSources();

            Assert.Matches(@"\bclass\s+ShippedRuntimeSafetyTests\b", sources);
            Assert.DoesNotMatch(@"\bclass\s+ThisGuardWasDeletedTests\b", sources);
        }
    }
}
