// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Pins the API-reference renderer's doc-XML LOAD against a fixture file (B23).
    ///     <para>
    ///         <see cref="GeneratedDocsAreCurrentTests.The_api_reference_matches_the_public_surface" /> cannot
    ///         see a defect in here: it compares the committed page against this very renderer's current output,
    ///         so a renderer that eats characters is self-consistent and stays green forever. That is how
    ///         <c>ApiReferenceRenderer</c> shipped for four rounds silently deleting the space between two
    ///         adjacent inline doc tags — <c>&lt;/b&gt; &lt;c&gt;</c> rendered as one welded word.
    ///     </para>
    ///     <para>
    ///         The pin therefore compares against a HAND-WRITTEN expectation, not against the renderer, and it
    ///         goes through the real file-loading path. A test that built an <c>XElement</c> in memory and
    ///         flattened it would have passed before the fix too: the defect was
    ///         <c>XDocument.Load</c>'s default <c>LoadOptions.None</c> discarding whitespace-only text nodes,
    ///         which happens during the load and is unrecoverable afterwards.
    ///     </para>
    /// </summary>
    public class ApiReferenceRendererTests
    {
        /// <summary>
        ///     The doc XML the C# compiler emits for a summary whose text separates two inline elements by
        ///     nothing but a space. Every pair below is a shape that exists verbatim in
        ///     <c>src/DwarfMapper</c>'s public doc comments today.
        /// </summary>
        private const string Fixture = """
                                       <?xml version="1.0"?>
                                       <doc>
                                           <assembly><name>Fixture</name></assembly>
                                           <members>
                                               <member name="T:Fixture.Adjacent">
                                                   <summary>
                                                       A <c>DWARF038</c> <b>build error</b> and a
                                                       <c>System.Text.Json</c> <c>IgnoreCycles</c> and
                                                       <b>constructed</b> <b>from the class</b> and
                                                       <c>tag</c> <see cref="T:System.String" /> too.
                                                   </summary>
                                               </member>
                                               <member name="P:Fixture.Adjacent.NoSummary">
                                                   <remarks>Ignored — only summaries are read.</remarks>
                                               </member>
                                           </members>
                                       </doc>
                                       """;

        [Fact]
        public void A_space_between_two_adjacent_inline_tags_survives_the_load()
        {
            InTempFile(Fixture,
                path =>
                {
                    var summaries = ApiReferenceRenderer.ParseSummaries(path);

                    Assert.Equal(
                        "A DWARF038 build error and a System.Text.Json IgnoreCycles and " + "constructed from the class and tag String too.",
                        summaries["T:Fixture.Adjacent"]);
                });
        }

        [Fact]
        public void A_member_without_a_summary_contributes_no_entry()
        {
            // The other half of the loop's guard: a member carrying only <remarks> must not land in the map as
            // an empty summary, which would render a blank cell that looks like undocumented code.
            InTempFile(Fixture,
                path =>
                    Assert.False(ApiReferenceRenderer.ParseSummaries(path).ContainsKey("P:Fixture.Adjacent.NoSummary")));
        }

        /// <summary>
        ///     Everything the reader SKIPS or REWRITES, none of which the fixture above reaches: a member
        ///     element with no name, the three shapes a see-tag can take, and the pipe escape that keeps a
        ///     summary from breaking the markdown table it is rendered into.
        /// </summary>
        private const string SeeTagFixture = """
                                             <?xml version="1.0"?>
                                             <doc>
                                                 <assembly><name>Fixture</name></assembly>
                                                 <members>
                                                     <member>
                                                         <summary>No name attribute at all.</summary>
                                                     </member>
                                                     <member name="T:Fixture.Crefs">
                                                         <summary>Qualified <see cref="T:DwarfMapper.MapConfig" />, bare <see cref="T:Bare" />, plain <see cref="Plain" />.</summary>
                                                     </member>
                                                     <member name="T:Fixture.Langword">
                                                         <summary>Pass <see langword="null" /> or <see langword="false" />.</summary>
                                                     </member>
                                                     <member name="T:Fixture.Pipe">
                                                         <summary>one | two
                                                             three</summary>
                                                     </member>
                                                 </members>
                                             </doc>
                                             """;

        /// <summary>A member the compiler never writes, but a hand-edited or merged doc file can carry.</summary>
        [Fact]
        public void A_member_without_a_name_contributes_no_entry()
        {
            InTempFile(SeeTagFixture,
                path =>
                {
                    var summaries = ApiReferenceRenderer.ParseSummaries(path);

                    Assert.Equal(["T:Fixture.Crefs", "T:Fixture.Langword", "T:Fixture.Pipe"],
                        summaries.Keys.OrderBy(k => k, StringComparer.Ordinal));
                });
        }

        /// <summary>
        ///     A cref is a doc-comment ID, and the page wants the readable tail of it: the last segment when it
        ///     has a namespace, the part after the two-character prefix when it does not, and the value itself
        ///     when it carries no prefix either.
        /// </summary>
        [Fact]
        public void A_cref_renders_as_its_readable_tail()
        {
            InTempFile(SeeTagFixture,
                path =>
                    Assert.Equal("Qualified MapConfig, bare Bare, plain Plain.",
                        ApiReferenceRenderer.ParseSummaries(path)["T:Fixture.Crefs"]));
        }

        /// <summary>
        ///     REGRESSION. A langword is a C# keyword, not an ID, and used to go through the cref path, which
        ///     strips two characters: "null" rendered as "ll" and "false" as "lse". No published page showed it
        ///     only because every langword in the reflected assembly sits in a param element, which this
        ///     renderer does not read - so the defect was waiting for the first one written into a summary.
        /// </summary>
        [Fact]
        public void A_langword_renders_as_the_keyword_itself()
        {
            InTempFile(SeeTagFixture,
                path =>
                    Assert.Equal("Pass null or false.",
                        ApiReferenceRenderer.ParseSummaries(path)["T:Fixture.Langword"]));
        }

        /// <summary>
        ///     A pipe would open a new cell in the markdown table the summary is rendered into, silently
        ///     shifting every column after it; the line break and indent would break the row outright.
        /// </summary>
        [Fact]
        public void A_pipe_is_escaped_and_whitespace_collapses()
        {
            InTempFile(SeeTagFixture,
                path =>
                    Assert.Equal(@"one \| two three", ApiReferenceRenderer.ParseSummaries(path)["T:Fixture.Pipe"]));
        }

        /// <summary>
        ///     ARCH-06: temp directory only, never the repository. Registered in
        ///     <see cref="RepoWriteGuardTests.Every_raw_write_api_use_in_the_test_tree_is_a_registered_pattern" />.
        /// </summary>
        private static void InTempFile(string content, Action<string> body)
        {
            var dir = Path.Combine(Path.GetTempPath(), "dwarfmapper-apiref-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "Fixture.xml");
                File.WriteAllText(path, content);
                body(path);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
