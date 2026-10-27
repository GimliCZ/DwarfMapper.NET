// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using System.Xml.Linq;
using DwarfMapper.Generator.Tests.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>Every silence states its reason where it is written.</b> These are round 30's Task 30.4
    ///     (<c>Issues/round30/PLAN-de-silencing.md</c>), never built until round 34. Round 34's inventory found what
    ///     that cost: suppressed ids with no reason, comments explaining ids that were no longer suppressed, a pragma
    ///     whose reason was the rule's own title, and three projects suppressing DWARF ids against the 2026-09-27
    ///     ruling.
    ///     <para>
    ///         What each mechanism needs, in the forms the repository already uses:
    ///         <list type="bullet">
    ///             <item>
    ///                 <c>&lt;NoWarn&gt;</c>: the XML comments directly above the element name every id it suppresses,
    ///                 and name no id it does not.
    ///             </item>
    ///             <item>
    ///                 <c>#pragma warning disable</c>: a <c>//</c> comment on the directive's line or on the line
    ///                 directly above it. Only real directives are read (the files are parsed), so a test's source
    ///                 string and a doc comment that mention the directive are not.
    ///             </item>
    ///             <item><c>[SuppressMessage]</c>: a non-empty <c>Justification</c>.</item>
    ///             <item><c>.globalconfig</c>: a <c>#</c> comment directly above every downgrade.</item>
    ///         </list>
    ///         Whether a reason is a good one is for a human; these scans hold that one is written, next to the silence.
    ///         Each rule has a positive control that proves it can fail.
    ///     </para>
    /// </summary>
    public sealed class SuppressionReasonScanTests
    {
        // Assembled, so that neither this file's text nor a hook reading new lines takes the controls below for
        // directives.
        private const string Disable = "#pragma warning " + "disable";

        private static readonly Regex DiagnosticId =
            new(@"\b(?:CA|CS|IDE|MA|RS|IL|NU|SYSLIB|DWARF)\d{3,5}\b", RegexOptions.CultureInvariant);

        private static readonly Regex Downgrade =
            new(@"^\s*dotnet_diagnostic\.[\w.-]+\.severity\s*=\s*(?:none|silent|suggestion)\b",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly string[] NotScanned =
            ["bin", "obj", ".git", ".claude", ".codex", ".vs", ".idea", "StrykerOutput", "artifacts", "TestResults", "node_modules"];

        // ── <NoWarn> ─────────────────────────────────────────────────────────────

        [Fact]
        public void Every_NoWarn_id_is_named_by_the_comment_above_it_and_that_comment_names_no_other()
        {
            var files = ProjectFiles().ToList();
            var elements = files.Sum(f => XDocument.Parse(File.ReadAllText(f)).Descendants().Count(e => e.Name.LocalName == "NoWarn"));
            Assert.True(elements >= 15, $"found only {elements} <NoWarn> element(s) in {files.Count} project file(s); this scan is reading the wrong thing.");

            var findings = files.SelectMany(f => NoWarnFindings(File.ReadAllText(f)).Select(x => Relative(f) + ": " + x)).ToList();

            Assert.True(findings.Count == 0,
                "<NoWarn> ids without a reason, or reasons for ids no longer suppressed:\n  " + string.Join("\n  ", findings) +
                "\nName each suppressed id, with why, in the XML comments directly above the element; delete what is not suppressed.");
        }

        [Fact]
        public void The_NoWarn_rule_flags_an_id_without_a_reason_and_a_reason_without_an_id()
        {
            const string project = """
                                   <Project>
                                     <PropertyGroup>
                                       <!-- CA1001: a reason. -->
                                       <!-- CA1002: a reason for an id that is no longer suppressed. -->
                                       <NoWarn>$(NoWarn);CA1001;CA1003</NoWarn>
                                     </PropertyGroup>
                                   </Project>
                                   """;

            var findings = NoWarnFindings(project);

            Assert.Equal(2, findings.Count);
            Assert.Contains(findings, f => f.Contains("CA1003", StringComparison.Ordinal) && f.Contains("no reason", StringComparison.Ordinal));
            Assert.Contains(findings, f => f.Contains("CA1002", StringComparison.Ordinal) && f.Contains("not suppress", StringComparison.Ordinal));
        }

        /// <summary>
        ///     The 2026-09-27 ruling ("no reason to do NoWarn with dwarf") applied to this repository's own projects:
        ///     a DWARF diagnostic is answered by a per-site statement of intent, or by a <c>.globalconfig</c> where no
        ///     intent reaches, never by a project-wide <c>NoWarn</c>. <see cref="SuppressionAdviceTests" /> holds the
        ///     docs to the same rule.
        /// </summary>
        [Fact]
        public void No_project_suppresses_a_DWARF_diagnostic_in_NoWarn()
        {
            var findings = ProjectFiles()
                .SelectMany(f => NoWarnIds(File.ReadAllText(f))
                    .Where(id => id.StartsWith("DWARF", StringComparison.OrdinalIgnoreCase))
                    .Select(id => Relative(f) + ": " + id))
                .ToList();

            Assert.True(findings.Count == 0,
                "DWARF ids suppressed in a project's <NoWarn>: [" + string.Join(", ", findings) + "]. State the intent at the " +
                "site, or downgrade the id in that project's .globalconfig with its reason above it.");
        }

        // ── #pragma warning disable ──────────────────────────────────────────────

        [Fact]
        public void Every_pragma_warning_disable_has_its_reason_on_its_line_or_the_line_above()
        {
            var sources = SourceFiles().ToList();
            Assert.True(sources.Count >= 500, $"found only {sources.Count} C# file(s); this scan is reading the wrong thing.");

            var directives = 0;
            var findings = new List<string>();
            foreach (var file in sources)
            {
                var text = File.ReadAllText(file);
                if (!text.Contains(Disable, StringComparison.Ordinal))
                {
                    continue;
                }

                var found = PragmaFindings(text, out var count);
                directives += count;
                findings.AddRange(found.Select(x => Relative(file) + ":" + x));
            }

            // Non-vacuity: string literals and doc comments hold the text too, and are rightly not counted.
            Assert.True(directives >= 25, $"parsed only {directives} real pragma directive(s); this scan is reading the wrong thing.");
            Assert.True(findings.Count == 0,
                "pragma suppressions with no // reason on the directive's line or the line directly above it:\n  " +
                string.Join("\n  ", findings));
        }

        [Fact]
        public void The_pragma_rule_flags_a_missing_or_detached_reason_and_reads_no_string_literal()
        {
            var source = $$""""
                           class A
                           {
                           {{Disable}} CA1001
                               int a;
                               // CA1002: a reason on its own line.
                           {{Disable}} CA1002
                               int b;
                           {{Disable}} CA1003 // CA1003: a reason on the directive.
                               int c;

                               // CA1005: a reason a blank line away from its directive.

                           {{Disable}} CA1005
                               string d = """
                           {{Disable}} CA1004
                           """;
                           }
                           """";

            var findings = PragmaFindings(source, out var directives);

            Assert.Equal(4, directives);
            Assert.Equal(2, findings.Count);
            Assert.Contains(findings, f => f.StartsWith("3:", StringComparison.Ordinal));
            Assert.Contains(findings, f => f.StartsWith("13:", StringComparison.Ordinal));
        }

        // ── [SuppressMessage] ────────────────────────────────────────────────────

        [Fact]
        public void Every_SuppressMessage_carries_a_Justification()
        {
            var attributes = 0;
            var findings = new List<string>();
            foreach (var file in SourceFiles())
            {
                var text = File.ReadAllText(file);
                if (!text.Contains("SuppressMessage", StringComparison.Ordinal))
                {
                    continue;
                }

                var found = SuppressMessageFindings(text, out var count);
                attributes += count;
                findings.AddRange(found.Select(x => Relative(file) + ":" + x));
            }

            Assert.True(attributes >= 3, $"parsed only {attributes} [SuppressMessage] attribute(s); this scan is reading the wrong thing.");
            Assert.True(findings.Count == 0,
                "[SuppressMessage] / [UnconditionalSuppressMessage] without a Justification:\n  " + string.Join("\n  ", findings));
        }

        [Fact]
        public void The_SuppressMessage_rule_flags_a_missing_and_an_empty_Justification()
        {
            const string source = """
                                  using System.Diagnostics.CodeAnalysis;
                                  [SuppressMessage("Design", "CA1001", Justification = "a reason")]
                                  [SuppressMessage("Design", "CA1002")]
                                  [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = " ")]
                                  class A { }
                                  """;

            var findings = SuppressMessageFindings(source, out var attributes);

            Assert.Equal(3, attributes);
            Assert.Equal(2, findings.Count);
        }

        // ── .globalconfig ────────────────────────────────────────────────────────

        [Fact]
        public void Every_globalconfig_downgrade_sits_under_a_comment()
        {
            var configs = GlobalConfigs().ToList();
            Assert.True(configs.Count >= 4, $"found only {configs.Count} .globalconfig file(s); this scan is reading the wrong thing.");

            var findings = configs.SelectMany(f => GlobalConfigFindings(File.ReadAllText(f)).Select(x => Relative(f) + ":" + x)).ToList();

            Assert.True(findings.Count == 0,
                ".globalconfig downgrades with no comment directly above them:\n  " + string.Join("\n  ", findings));
        }

        [Fact]
        public void The_globalconfig_rule_flags_a_downgrade_with_no_comment_above_it()
        {
            const string config = """
                                  is_global = true

                                  # CA1001: a reason.
                                  dotnet_diagnostic.CA1001.severity = none
                                  dotnet_diagnostic.CA1002.severity = suggestion

                                  dotnet_diagnostic.CA1003.severity = error
                                  """;

            var findings = GlobalConfigFindings(config);

            Assert.Equal("5: dotnet_diagnostic.CA1002.severity = suggestion", Assert.Single(findings));
        }

        // ── The rules ────────────────────────────────────────────────────────────

        private static IEnumerable<string> Ids(XElement noWarn)
        {
            return noWarn.Value.Split([';', ',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Where(token => !token.StartsWith("$(", StringComparison.Ordinal));
        }

        private static List<string> NoWarnIds(string projectXml)
        {
            return XDocument.Parse(projectXml).Descendants().Where(e => e.Name.LocalName == "NoWarn").SelectMany(Ids).ToList();
        }

        private static List<string> NoWarnFindings(string projectXml)
        {
            var findings = new List<string>();
            foreach (var element in XDocument.Parse(projectXml).Descendants().Where(e => e.Name.LocalName == "NoWarn"))
            {
                // The reason block: the comments directly above the element, with nothing but whitespace between.
                var block = string.Join("\n", element.NodesBeforeSelf().Reverse()
                    .TakeWhile(n => n is XComment || (n is XText t && string.IsNullOrWhiteSpace(t.Value)))
                    .OfType<XComment>()
                    .Select(c => c.Value));
                var suppressed = Ids(element).ToHashSet(StringComparer.Ordinal);
                var named = DiagnosticId.Matches(block).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

                findings.AddRange(suppressed.Where(id => !named.Contains(id)).OrderBy(id => id, StringComparer.Ordinal)
                    .Select(id => $"{id} is suppressed with no reason above it"));
                findings.AddRange(named.Where(id => !suppressed.Contains(id)).OrderBy(id => id, StringComparer.Ordinal)
                    .Select(id => $"the comment above <NoWarn> names {id}, which it does not suppress"));
            }

            return findings;
        }

        /// <summary>The 1-based lines of real pragma directives with no reason on their line or the line above.</summary>
        private static List<string> PragmaFindings(string text, out int directives)
        {
            var lines = text.Split('\n');
            var tree = CSharpSyntaxTree.ParseText(text, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
            var findings = new List<string>();
            directives = 0;
            foreach (var trivia in tree.GetRoot().DescendantTrivia(descendIntoTrivia: true))
            {
                if (!trivia.IsKind(SyntaxKind.PragmaWarningDirectiveTrivia) ||
                    trivia.GetStructure() is not PragmaWarningDirectiveTriviaSyntax pragma ||
                    !pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword))
                {
                    continue;
                }

                directives++;
                var line = pragma.GetLocation().GetLineSpan().StartLinePosition.Line;
                var above = line > 0 ? lines[line - 1].Trim() : "";
                var reasonAbove = above.StartsWith("//", StringComparison.Ordinal) && !above.StartsWith("///", StringComparison.Ordinal);
                var reasonOnLine = lines[line].Contains("//", StringComparison.Ordinal);
                if (!reasonAbove && !reasonOnLine)
                {
                    findings.Add($"{line + 1}: {lines[line].Trim()}");
                }
            }

            return findings;
        }

        private static List<string> SuppressMessageFindings(string text, out int attributes)
        {
            var tree = CSharpSyntaxTree.ParseText(text, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
            var findings = new List<string>();
            attributes = 0;
            foreach (var attribute in tree.GetRoot().DescendantNodes().OfType<AttributeSyntax>())
            {
                var name = attribute.Name is QualifiedNameSyntax q ? q.Right.Identifier.Text : attribute.Name.ToString();
                if (name is not ("SuppressMessage" or "SuppressMessageAttribute" or "UnconditionalSuppressMessage"
                    or "UnconditionalSuppressMessageAttribute"))
                {
                    continue;
                }

                attributes++;
                var justified = attribute.ArgumentList?.Arguments.Any(a =>
                    a.NameEquals?.Name.Identifier.Text == "Justification" &&
                    !string.IsNullOrWhiteSpace(ConstantText(a.Expression))) == true;
                if (!justified)
                {
                    findings.Add($"{attribute.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: {attribute}");
                }
            }

            return findings;
        }

        /// <summary>
        ///     The text a Justification spells out: a literal, or literals joined with <c>+</c> (how a long reason is
        ///     written). Anything else, a <c>const</c> or a <c>nameof</c>, is a reason held elsewhere, so it is taken as
        ///     written.
        /// </summary>
        private static string ConstantText(ExpressionSyntax expression)
        {
            return expression switch
            {
                LiteralExpressionSyntax literal => literal.Token.ValueText,
                BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } add => ConstantText(add.Left) + ConstantText(add.Right),
                ParenthesizedExpressionSyntax parenthesized => ConstantText(parenthesized.Expression),
                _ => expression.ToString()
            };
        }

        private static List<string> GlobalConfigFindings(string text)
        {
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            var findings = new List<string>();
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Downgrade.IsMatch(lines[i]))
                {
                    continue;
                }

                var above = i > 0 ? lines[i - 1].TrimStart() : "";
                if (!above.StartsWith('#') && !above.StartsWith(';'))
                {
                    findings.Add($"{i + 1}: {lines[i].Trim()}");
                }
            }

            return findings;
        }

        // ── What is scanned ──────────────────────────────────────────────────────

        private static IEnumerable<string> ProjectFiles()
        {
            return Walk(RepoPaths.Root).Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                                                   f.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                                                   f.EndsWith(".targets", StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> GlobalConfigs()
        {
            return Walk(RepoPaths.Root).Where(f => Path.GetFileName(f) == ".globalconfig");
        }

        private static IEnumerable<string> SourceFiles()
        {
            return new[] { "src", "tests", "samples", "benchmarks" }
                .SelectMany(d => Walk(Path.Combine(RepoPaths.Root, d)))
                .Where(f => f.EndsWith(".cs", StringComparison.Ordinal));
        }

        private static IEnumerable<string> Walk(string dir)
        {
            if (!Directory.Exists(dir))
            {
                yield break;
            }

            foreach (var file in Directory.EnumerateFiles(dir))
                yield return file;
            foreach (var sub in Directory.EnumerateDirectories(dir))
                if (!NotScanned.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var file in Walk(sub))
                        yield return file;
                }
        }

        private static string Relative(string path) => Path.GetRelativePath(RepoPaths.Root, path).Replace('\\', '/');
    }
}
