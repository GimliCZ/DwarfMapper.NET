// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// MemberFacts decides which members the generated code may touch. Three of its answers no mapper fixture reached:
// - the no-compilation opt-in: every [DwarfMapper] caller passes its compilation, and the one caller that passes
//   none ([MapTo]) never opts into non-public members, so "internal, no context" was never asked;
// - the cross-assembly [InternalsVisibleTo] question, which needs a member declared in ANOTHER compilation;
// - a static member declared on an interface, which the interface walk must skip.
// All three are asked here directly, through the same internal entry points the generator calls.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MemberFactsUnitTests
    {
        private static readonly MetadataReference[] References = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToArray();

        private static CSharpCompilation Compile(string name, string source, params MetadataReference[] extra)
        {
            return CSharpCompilation.Create(name,
                [CSharpSyntaxTree.ParseText(source)],
                References.Concat(extra),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        }

        private static List<string> ReadableNames(ITypeSymbol type, Compilation? compilation, bool allowNonPublic)
        {
            return MemberFacts.Readable(type, compilation, allowNonPublic).Select(m => m.Name).ToList();
        }

        [Fact]
        public void Without_a_compilation_an_opted_in_internal_member_is_assumed_to_be_in_the_same_assembly()
        {
            var compilation = Compile("NoContext", "namespace T { public class S { internal int Hidden { get; set; } public int Shown { get; set; } } }");
            var type = compilation.GetTypeByMetadataName("T.S")!;

            Assert.Contains("Hidden", ReadableNames(type, null, allowNonPublic: true));
            Assert.DoesNotContain("Hidden", ReadableNames(type, null, allowNonPublic: false));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void An_internal_member_of_another_assembly_is_reachable_only_through_InternalsVisibleTo(bool grantsAccess)
        {
            var attribute = grantsAccess
                ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Consumer\")]\n"
                : "";
            var library = Compile("Library", attribute + "namespace L { public class S { internal int Hidden { get; set; } } }");
            var consumer = Compile("Consumer", "namespace C { }", library.ToMetadataReference());
            var type = consumer.GetTypeByMetadataName("L.S")!;

            Assert.Equal(grantsAccess, ReadableNames(type, consumer, allowNonPublic: true).Contains("Hidden"));
        }

        [Fact]
        public void No_containing_assembly_grants_no_access()
        {
            var consumer = Compile("Consumer", "namespace C { }");

            Assert.False(MemberFacts.AssemblyGrantsAccess(null, consumer.Assembly));
            Assert.True(MemberFacts.AssemblyGrantsAccess(consumer.Assembly, consumer.Assembly));
        }

        [Fact]
        public void A_static_member_declared_on_an_interface_is_not_readable_off_an_instance()
        {
            var compilation = Compile("InterfaceStatic",
                "namespace T { public interface IShape { static int Sides { get; } = 3; int Id { get; } } }");
            var type = compilation.GetTypeByMetadataName("T.IShape")!;

            Assert.Equal(["Id"], ReadableNames(type, compilation, allowNonPublic: false));
        }
    }
}
