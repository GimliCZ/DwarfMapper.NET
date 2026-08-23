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
