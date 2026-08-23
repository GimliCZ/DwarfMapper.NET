// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     No emitter may write a member name into generated code without escaping it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Member names reach emitted C# from ~40 places. A DTO member called <c>@class</c> arrives from
    ///         <c>ISymbol.Name</c> as <c>class</c> — the escape is syntax, not part of the name — so any site that
    ///         writes the raw name produces <c>class = src.class,</c>, which the compiler parses as a malformed
    ///         event declaration with an EMPTY member name, out of generated code, with no DwarfMapper diagnostic.
    ///     </para>
    ///     <para>
    ///         Fixing 40 of 41 sites is the failure mode this scan exists for, and it is not hypothetical: an
    ///         earlier audit in this project found a fix applied to one of N identical construction sites. So the
    ///         rule is mechanical rather than reviewed — an emitter file may name <c>EmitTargetName</c> /
    ///         <c>EmitSourceName</c>, never the raw pair.
    ///     </para>
    /// </remarks>
    public class EmittedIdentifiersAreEscapedTests
    {
        /// <summary>Files whose job is to WRITE C#. Extractors that only compare or report are not scanned.</summary>
        private static readonly string[] EmitterFiles =
        [
            Path.Combine("Pipeline", "MapEmitter.cs"),
            Path.Combine("Pipeline", "MapperExtractor.Flatten.cs")
        ];

        /// <summary>
        ///     Uses of the raw name that are demonstrably NOT emission, each with its reason.
        /// </summary>
        /// <remarks>
        ///     Kept as line-anchored text rather than a line number so it cannot rot into pointing at whatever
        ///     happens to be on that line later.
        /// </remarks>
        private static readonly string[] NotEmission =
        [
            // Looks a member up on the SOURCE TYPE by name — a Roslyn lookup, where "@class" would find nothing.
            "MemberTypeByName(srcType, members[idx].SourceName)",

            // Tests a LENGTH, not a name. The empty target name is the constructor-only projection sentinel; the
            // escaped form has the same length for every name that is not a keyword and one more for those that
            // are, so reading it here would be both pointless and subtly wrong.
            "projMembers[0].TargetName.Length == 0"
        ];

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DwarfMapper.NET.sln")))
                dir = dir.Parent;

            Assert.True(dir is not null, "Could not locate the repository root from the test output directory.");
            return dir.FullName;
        }

        [Fact]
        public void No_emitter_writes_a_raw_member_name()
        {
            var root = Path.Combine(RepoRoot(), "src", "DwarfMapper.Generator");
            var raw = new Regex(@"\.(TargetName|SourceName)\b");
            var offenders = new List<string>();

            foreach (var relative in EmitterFiles)
            {
                var path = Path.Combine(root, relative);
                Assert.True(File.Exists(path), $"Emitter file not found — has it moved? {relative}");

                var lines = File.ReadAllLines(path);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (!raw.IsMatch(line))
                    {
                        continue;
                    }

                    if (line.Contains("EmitTargetName", StringComparison.Ordinal) || line.Contains("EmitSourceName", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (Array.Exists(NotEmission, e => line.Contains(e, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    offenders.Add($"{relative}:{i + 1}  {line.Trim()}");
                }
            }

            Assert.True(offenders.Count == 0,
                "Emitter site(s) writing a RAW member name into generated code:\n  " +
                string.Join("\n  ", offenders) +
                "\n\nUse EmitTargetName / EmitSourceName, which escape reserved keywords per path segment. A DTO " +
                "member called @class arrives from ISymbol.Name as 'class', and writing that produces " +
                "'class = src.class,' — parsed as a malformed event declaration, out of generated code, with no " +
                "DwarfMapper diagnostic.\n\nIf the use genuinely is not emission (a Roslyn lookup by name, say), " +
                "add it to NotEmission with the reason.");
        }

        [Fact]
        public void The_exemption_list_still_matches_real_code()
        {
            // An exemption that no longer matches anything is a claim about the code that has stopped being true,
            // and it would silently keep covering whatever moves onto that line next.
            var root = Path.Combine(RepoRoot(), "src", "DwarfMapper.Generator");
            var all = string.Join("\n", EmitterFiles.Select(f => File.ReadAllText(Path.Combine(root, f))));

            var stale = NotEmission.Where(e => !all.Contains(e, StringComparison.Ordinal)).ToList();

            Assert.True(stale.Count == 0,
                "Exemption(s) that match no line any more — delete them:\n  " + string.Join("\n  ", stale));
        }
    }
}
