// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// MemberDirectives.Read returns a member's [MapProperty] / [MapIgnore] applications in the order that binds them to
// targets. Two of its answers had no fixture:
// - any OTHER attribute on the member is not a directive and is skipped;
// - applications split across two FILES (a C# 13 partial property carries attributes on both parts) are ordered by
//   file path first, so the order does not depend on which tree the compiler happened to enumerate first.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MemberDirectivesUnitTests
    {
        [Fact]
        public void An_attribute_that_is_not_a_member_directive_is_skipped()
        {
            var compilation = GeneratorTestHarness.BuildCompilation("MemberDirectivesOther", """
                using DwarfMapper;
                namespace T
                {
                    public class Host
                    {
                        [System.ComponentModel.Description("not a directive")]
                        [MapProperty("Source")]
                        public int Target { get; set; }
                    }
                }
                """);
            var member = compilation.GetTypeByMetadataName("T.Host")!.GetMembers("Target").Single();

            var directive = Assert.Single(MemberDirectives.Read(member));
            Assert.False(directive.Ignore);
            Assert.Equal("Source", directive.Name);
        }

        [Fact]
        public void Directives_in_two_files_are_ordered_by_file_before_position()
        {
            // B.cs's application sits at a SMALLER offset than A.cs's; ordering by position alone would put it first.
            var declaration = CSharpSyntaxTree.ParseText("""
                                                         using DwarfMapper;
                                                         namespace T
                                                         {
                                                             public partial class Host
                                                             {
                                                                 // padding so this application starts well past the other file's
                                                                 [MapProperty("FromA")] public partial int Target { get; set; }
                                                             }
                                                         }
                                                         """, path: "A.cs");
            var implementation = CSharpSyntaxTree.ParseText("""
                                                            using DwarfMapper;
                                                            namespace T { public partial class Host { [MapProperty("FromB")] public partial int Target { get => 0; set { } } } }
                                                            """, path: "B.cs");
            var compilation = GeneratorTestHarness.BuildCompilation("MemberDirectivesFiles", [implementation, declaration]);
            var member = compilation.GetTypeByMetadataName("T.Host")!.GetMembers("Target").OfType<IPropertySymbol>().Single();

            Assert.Equal("FromA|FromB", string.Join("|", MemberDirectives.Read(member).Select(d => d.Name)));
        }
    }
}
