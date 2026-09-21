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

        /// <summary>
        ///     A public type nested in a public type. GetExportedTypes returns it, so the page has to decide
        ///     whether to render it, and that decision was reading the wrong flag.
        /// </summary>
        // CA1034 asks for these not to be nested, and CA1812 for the internal one to be removed as
        // uninstantiated. A PUBLIC NESTED TYPE IS THE SHAPE UNDER TEST: the filter reads the wrong flag for
        // exactly this declaration, and the internal sibling is the arm that must stay excluded. Both exist as
        // Type arguments only, never as instances.
#pragma warning disable CA1034, CA1812
        public sealed class OuterProbe
        {
            public sealed class NestedPublicProbe
            {
            }

            internal sealed class NestedInternalProbe
            {
            }
        }
#pragma warning restore CA1034, CA1812

        // CA1034/CA1812 again, and for the same reason as OuterProbe above: these exist as Type arguments,
        // never as instances, and their SHAPE is what the method under test branches on.
#pragma warning disable CA1034, CA1812
        public abstract class AbstractProbe
        {
            public int Value { get; set; }
        }

        public interface IShapeProbe
        {
            int Value { get; }
        }

        /// <summary>A type whose construction fails - the reason the renderer catches at all.</summary>
        public sealed class ThrowingConstructorProbe
        {
            public ThrowingConstructorProbe()
            {
                throw new InvalidOperationException("this type refuses to be constructed");
            }

            public int Value { get; set; }
        }

        /// <summary>The ordinary case: a type whose property defaults CAN be read.</summary>
        public sealed class DefaultsProbe
        {
            public int Value { get; set; } = 7;
        }
#pragma warning restore CA1034, CA1812

        /// <summary>
        ///     An abstract type and an interface have no instance to read defaults from, so the page reports
        ///     "—" for their members rather than crashing the whole reference.
        /// </summary>
        [Fact]
        public void An_abstract_type_and_an_interface_have_no_defaults_instance()
        {
            Assert.Null(ApiReferenceRenderer.TryCreateDefaults(typeof(AbstractProbe)));
            Assert.Null(ApiReferenceRenderer.TryCreateDefaults(typeof(IShapeProbe)));
        }

        /// <summary>
        ///     A constructor that throws must not take the page down with it. The catch is what turns one
        ///     uncooperative type into one "—" cell instead of a failed documentation build.
        /// </summary>
        [Fact]
        public void A_constructor_that_throws_leaves_the_defaults_unread()
        {
            Assert.Null(ApiReferenceRenderer.TryCreateDefaults(typeof(ThrowingConstructorProbe)));
        }

        /// <summary>The positive control: without this, the three nulls above would pass on a method that always returns null.</summary>
        [Fact]
        public void An_ordinary_type_yields_an_instance_to_read_defaults_from()
        {
            var instance = ApiReferenceRenderer.TryCreateDefaults(typeof(DefaultsProbe));

            Assert.Equal(7, Assert.IsType<DefaultsProbe>(instance).Value);
        }

        /// <summary>
        ///     REGRESSION. The type filter asked Type.IsPublic, which is FALSE for every nested type however
        ///     visible - the nested flavour is IsNestedPublic - so a public nested type was dropped from the API
        ///     reference without a word. Latent today: the runtime assembly's only nested types are private, so
        ///     no row is missing from the committed page. It would have bitten the first public nested type.
        /// </summary>
        [Fact]
        public void A_public_nested_type_belongs_on_the_page()
        {
            Assert.True(ApiReferenceRenderer.IsRenderableType(typeof(OuterProbe.NestedPublicProbe)));
        }

        /// <summary>The ordinary case, and the arm every rendered page is built from.</summary>
        [Fact]
        public void A_top_level_type_belongs_on_the_page()
        {
            Assert.True(ApiReferenceRenderer.IsRenderableType(typeof(ApiReferenceRenderer)));
        }

        /// <summary>
        ///     The arm the filter exists for. GetExportedTypes never hands this one over, so the predicate is
        ///     the only place the rule can be stated and the only place it can be checked.
        /// </summary>
        [Fact]
        public void A_nested_type_that_is_not_public_does_not()
        {
            Assert.False(ApiReferenceRenderer.IsRenderableType(typeof(OuterProbe.NestedInternalProbe)));
        }

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
