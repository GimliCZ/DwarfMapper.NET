// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using DwarfMapper.CodeFixes;
using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DwarfMapper.Generator.Tests.CodeFixes
{
    /// <summary>
    ///     What the DWARF085 restatement actually COPIES, and what it leaves alone.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The existing end-to-end test drives one shape: the derived pair already configures the same member
    ///         differently, so the fix REPLACES an attribute. That shape never reaches the other half of the
    ///         code — a member the derived pair does not configure at all, which is ADDED — nor any attribute
    ///         kind but <c>[MapProperty]</c>, nor the check that decides which attributes belong to the base pair
    ///         in the first place.
    ///     </para>
    ///     <para>
    ///         These tests use synthetic diagnostics so each shape can be reached directly. They assert on the
    ///         text the fix produces, because that text is what lands in the consumer's file.
    ///     </para>
    /// </remarks>
    public class RestateBaseRestatementTests
    {
        /// <summary>Base configures two members; the derived pair configures only the first, identically.</summary>
        private const string AdditionSrc = """
                                           using DwarfMapper;
                                           namespace Demo;
                                           public class Command { public string Raw { get; set; } = ""; public string Note { get; set; } = ""; }
                                           public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                                           public class CommandDto { public string Text { get; set; } = ""; public string Memo { get; set; } = ""; }
                                           public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                                           [DwarfMapper]
                                           [GenerateMap<Command, CommandDto>]
                                           [MapProperty<Command, CommandDto>("Raw", "Text")]
                                           [MapProperty<Command, CommandDto>("Note", "Memo")]
                                           [GenerateMap<AliasCommand, AliasCommandDto>]
                                           [RestatesBase<AliasCommand, AliasCommandDto>]
                                           [MapProperty<AliasCommand, AliasCommandDto>("Raw", "Text")]
                                           public partial class M
                                           {
                                           }
                                           """;

        private static async Task<string> RestateAsync(string source, string atAttribute)
        {
            var i = source.IndexOf(atAttribute, StringComparison.Ordinal);
            Assert.True(i >= 0, $"test source does not contain '{atAttribute}'");

            var tree = CSharpSyntaxTree.ParseText(source);
            var diagnostic = Diagnostic.Create(DiagnosticDescriptors.RestatedBaseDrift,
                Location.Create(tree, new TextSpan(i, atAttribute.Length)),
                ImmutableDictionary.CreateRange([
                    new KeyValuePair<string, string?>("SourcePair", "Demo.Command|Demo.CommandDto")
                ]),
                "AliasCommand",
                "AliasCommandDto");

            using var workspace = new AdhocWorkspace();
            var document = workspace.AddProject("FixAsm", LanguageNames.CSharp).AddDocument("M.cs", source);

            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), CancellationToken.None);
            await new RestateBaseConfigurationCodeFixProvider().RegisterCodeFixesAsync(context)
                .ConfigureAwait(false);

            var operations = await Assert.Single(actions).GetOperationsAsync(CancellationToken.None)
                .ConfigureAwait(false);
            var changed = operations.OfType<ApplyChangesOperation>().Single()
                .ChangedSolution.GetDocument(document.Id)!;
            return (await changed.GetTextAsync().ConfigureAwait(false)).ToString();
        }

        /// <summary>
        ///     A member the derived pair does not configure AT ALL is added, not merely replaced. This is the
        ///     half of the fix the existing end-to-end test cannot reach, because its derived pair already
        ///     configures the one member the base does.
        /// </summary>
        [Fact]
        public async Task A_member_the_derived_pair_does_not_configure_is_ADDED()
        {
            var fixedText = await RestateAsync(AdditionSrc,
                    "MapProperty<AliasCommand, AliasCommandDto>(\"Raw\", \"Text\")")
                .ConfigureAwait(true);

            Assert.Contains("MapProperty<AliasCommand, AliasCommandDto>(\"Note\", \"Memo\")", fixedText,
                StringComparison.Ordinal);

            // The member it already configured identically is not duplicated.
            Assert.Equal(1,
                fixedText.Split("MapProperty<AliasCommand, AliasCommandDto>(\"Raw\", \"Text\")").Length - 1);
        }

        /// <summary>
        ///     <c>[MapIgnore]</c> is pair-scoped too, and carries ONE type argument rather than two. Restating a
        ///     base that ignores a member must reproduce the ignore — and must not index a second type argument
        ///     that is not there.
        /// </summary>
        [Fact]
        public async Task A_base_MapIgnore_is_restated_despite_carrying_one_type_argument()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Command { public string Raw { get; set; } = ""; }
                               public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                               public class CommandDto { public string Text { get; set; } = ""; public string Skip { get; set; } = ""; }
                               public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                               [DwarfMapper]
                               [GenerateMap<Command, CommandDto>]
                               [MapIgnore<CommandDto>("Skip")]
                               [GenerateMap<AliasCommand, AliasCommandDto>]
                               [RestatesBase<AliasCommand, AliasCommandDto>]
                               [MapProperty<AliasCommand, AliasCommandDto>("Raw", "Text")]
                               public partial class M
                               {
                               }
                               """;

            var fixedText = await RestateAsync(src,
                "MapProperty<AliasCommand, AliasCommandDto>(\"Raw\", \"Text\")").ConfigureAwait(true);

            Assert.Contains("MapIgnore<AliasCommandDto>(\"Skip\")", fixedText, StringComparison.Ordinal);
        }

        /// <summary><c>[MapValue]</c> is the third pair-scoped kind, and is restated on the same terms.</summary>
        [Fact]
        public async Task A_base_MapValue_is_restated()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Command { public string Raw { get; set; } = ""; }
                               public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                               public class CommandDto { public string Text { get; set; } = ""; public string Tag { get; set; } = ""; }
                               public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                               [DwarfMapper]
                               [GenerateMap<Command, CommandDto>]
                               [MapValue<CommandDto>("Tag", "fixed")]
                               [GenerateMap<AliasCommand, AliasCommandDto>]
                               [RestatesBase<AliasCommand, AliasCommandDto>]
                               [MapProperty<AliasCommand, AliasCommandDto>("Raw", "Text")]
                               public partial class M
                               {
                               }
                               """;

            var fixedText = await RestateAsync(src,
                "MapProperty<AliasCommand, AliasCommandDto>(\"Raw\", \"Text\")").ConfigureAwait(true);

            Assert.Contains("MapValue<AliasCommandDto>(\"Tag\", \"fixed\")", fixedText, StringComparison.Ordinal);
        }

        /// <summary>
        ///     An attribute written with its FULL name — <c>[MapPropertyAttribute&lt;…&gt;]</c>, which C# allows
        ///     everywhere the short form is allowed — is still pair-scoped and still restated.
        /// </summary>
        [Fact]
        public async Task A_base_attribute_written_with_its_full_Attribute_suffix_is_restated()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Command { public string Raw { get; set; } = ""; public string Note { get; set; } = ""; }
                               public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                               public class CommandDto { public string Text { get; set; } = ""; public string Memo { get; set; } = ""; }
                               public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                               [DwarfMapper]
                               [GenerateMap<Command, CommandDto>]
                               [MapPropertyAttribute<Command, CommandDto>("Note", "Memo")]
                               [GenerateMap<AliasCommand, AliasCommandDto>]
                               [RestatesBase<AliasCommand, AliasCommandDto>]
                               [MapProperty<AliasCommand, AliasCommandDto>("Raw", "Text")]
                               public partial class M
                               {
                               }
                               """;

            var fixedText = await RestateAsync(src,
                "MapProperty<AliasCommand, AliasCommandDto>(\"Raw\", \"Text\")").ConfigureAwait(true);

            // The RESTATED form, not just the member names -- those already appear on the base attribute,
            // so looking for them anywhere in the file asserted nothing.
            Assert.Contains("AliasCommandDto>(\"Note\", \"Memo\")", fixedText, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Attributes written NAMESPACE-QUALIFIED — <c>[DwarfMapper.MapProperty&lt;…&gt;]</c>, legal wherever the
        ///     short form is — are still pair-scoped and still restated. Every helper that reads an attribute's name
        ///     peels a <c>QualifiedNameSyntax</c> to its right-hand side first; the diagnostic here sits on a qualified
        ///     derived attribute too, so the derived pair is read through the same arm.
        /// </summary>
        [Fact]
        public async Task Namespace_qualified_attributes_are_read_and_restated()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Command { public string Raw { get; set; } = ""; public string Note { get; set; } = ""; }
                               public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                               public class CommandDto { public string Text { get; set; } = ""; public string Memo { get; set; } = ""; }
                               public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                               [DwarfMapper]
                               [GenerateMap<Command, CommandDto>]
                               [DwarfMapper.MapProperty<Command, CommandDto>("Note", "Memo")]
                               [GenerateMap<AliasCommand, AliasCommandDto>]
                               [RestatesBase<AliasCommand, AliasCommandDto>]
                               [DwarfMapper.MapProperty<AliasCommand, AliasCommandDto>("Raw", "Text")]
                               public partial class M
                               {
                               }
                               """;

            var fixedText = await RestateAsync(src,
                "DwarfMapper.MapProperty<AliasCommand, AliasCommandDto>(\"Raw\", \"Text\")").ConfigureAwait(true);

            // Retargeted onto the derived pair: the type arguments were rewritten through the qualified name's right side.
            Assert.Contains("MapProperty<AliasCommand, AliasCommandDto>(\"Note\", \"Memo\")", fixedText,
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     An attribute for a DIFFERENT pair is not the base's, and must not be dragged into the
        ///     restatement. Both type arguments have to match, not either one.
        /// </summary>
        [Fact]
        public async Task An_attribute_for_a_different_pair_is_not_restated()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Command { public string Raw { get; set; } = ""; public string Note { get; set; } = ""; }
                               public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                               public class CommandDto { public string Text { get; set; } = ""; }
                               public class OtherDto { public string Memo { get; set; } = ""; }
                               public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                               [DwarfMapper]
                               [GenerateMap<Command, CommandDto>]
                               [MapProperty<Command, CommandDto>("Raw", "Text")]
                               [MapProperty<Command, OtherDto>("Note", "Memo")]
                               [GenerateMap<AliasCommand, AliasCommandDto>]
                               [RestatesBase<AliasCommand, AliasCommandDto>]
                               [MapProperty<AliasCommand, AliasCommandDto>("Note", "Text")]
                               public partial class M
                               {
                               }
                               """;

            var fixedText = await RestateAsync(src,
                "MapProperty<AliasCommand, AliasCommandDto>(\"Note\", \"Text\")").ConfigureAwait(true);

            // Command -> OtherDto shares only its SOURCE with the base pair, so it is not part of it.
            Assert.DoesNotContain("AliasCommandDto>(\"Note\"", fixedText, StringComparison.Ordinal);

            // And the attribute that IS the base pair's did get restated OVER the derived one. Both configure
            // the same destination member ("Text"), so this is a replacement rather than an addition -- and the
            // two texts differ only in a value, with identical whitespace, which is what makes it also pin the
            // whitespace-insensitive comparison that decides whether a replacement is needed at all.
            Assert.Contains("AliasCommandDto>(\"Raw\", \"Text\")", fixedText, StringComparison.Ordinal);
            Assert.DoesNotContain("AliasCommandDto>(\"Note\", \"Text\")", fixedText, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Which positional argument names the DESTINATION member differs by attribute kind:
        ///     <c>[MapProperty(source, target)]</c> names it second, <c>[MapValue(target, value)]</c> first.
        ///     Getting that wrong does not crash — it keys the attribute by its VALUE, so a drifted member is
        ///     never recognised as already configured and the fix appends a duplicate instead of replacing it.
        /// </summary>
        [Fact]
        public async Task A_drifted_MapValue_is_replaced_rather_than_duplicated()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Command { public string Raw { get; set; } = ""; }
                               public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                               public class CommandDto { public string Text { get; set; } = ""; public string Tag { get; set; } = ""; }
                               public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                               [DwarfMapper]
                               [GenerateMap<Command, CommandDto>]
                               [MapProperty<Command, CommandDto>("Raw", "Text")]
                               [MapValue<CommandDto>("Tag", "fixed")]
                               [GenerateMap<AliasCommand, AliasCommandDto>]
                               [RestatesBase<AliasCommand, AliasCommandDto>]
                               [MapProperty<AliasCommand, AliasCommandDto>("Raw", "Text")]
                               [MapValue<AliasCommandDto>("Tag", "other")]
                               public partial class M
                               {
                               }
                               """;

            var fixedText = await RestateAsync(src,
                "MapProperty<AliasCommand, AliasCommandDto>(\"Raw\", \"Text\")").ConfigureAwait(true);

            Assert.Contains("MapValue<AliasCommandDto>(\"Tag\", \"fixed\")", fixedText, StringComparison.Ordinal);
            Assert.DoesNotContain("\"other\"", fixedText, StringComparison.Ordinal);
        }

        /// <summary>Base pair-scoped attribute with no argument list at all: it names no member, so it is added.</summary>
        /// <remarks>
        ///     Odd to write by hand, but syntactically legal, and a code fix reads SYNTAX — it has no compiler to
        ///     tell it the attribute is unusable. Reading the arguments without checking for their absence throws
        ///     inside the user's lightbulb.
        /// </remarks>
        [Fact]
        public async Task A_base_attribute_with_no_arguments_names_no_member_and_is_added()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Command { public string Raw { get; set; } = ""; }
                               public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                               public class CommandDto { public string Text { get; set; } = ""; }
                               public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                               [DwarfMapper]
                               [GenerateMap<Command, CommandDto>]
                               [MapIgnore<CommandDto>]
                               [GenerateMap<AliasCommand, AliasCommandDto>]
                               [RestatesBase<AliasCommand, AliasCommandDto>]
                               [MapProperty<AliasCommand, AliasCommandDto>("Raw", "Text")]
                               public partial class M
                               {
                               }
                               """;

            var fixedText = await RestateAsync(src,
                "MapProperty<AliasCommand, AliasCommandDto>(\"Raw\", \"Text\")").ConfigureAwait(true);

            Assert.Contains("MapIgnore<AliasCommandDto>", fixedText, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Base attribute whose arguments are ALL named: there is no positional argument to read a member
        ///     from, and indexing the first one anyway throws.
        /// </summary>
        [Fact]
        public async Task A_base_attribute_with_only_named_arguments_names_no_member_and_is_added()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Command { public string Raw { get; set; } = ""; }
                               public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                               public class CommandDto { public string Text { get; set; } = ""; }
                               public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                               [DwarfMapper]
                               [GenerateMap<Command, CommandDto>]
                               [MapProperty<Command, CommandDto>(Use = nameof(Clean))]
                               [GenerateMap<AliasCommand, AliasCommandDto>]
                               [RestatesBase<AliasCommand, AliasCommandDto>]
                               [MapProperty<AliasCommand, AliasCommandDto>("Raw", "Text")]
                               public partial class M
                               {
                                   private static string Clean(string raw) => raw.Trim();
                               }
                               """;

            var fixedText = await RestateAsync(src,
                "MapProperty<AliasCommand, AliasCommandDto>(\"Raw\", \"Text\")").ConfigureAwait(true);

            Assert.Contains("AliasCommandDto>(Use = nameof(Clean))", fixedText, StringComparison.Ordinal);
        }
    }
}
