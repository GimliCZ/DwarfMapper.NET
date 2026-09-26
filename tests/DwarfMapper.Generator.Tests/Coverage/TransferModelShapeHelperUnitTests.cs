// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for two TransferModelShape questions no classified type answers both ways (per-branch rule: extract and
// test directly):
//   - AssemblyDisplayName without an assembly: a metadata type, the only kind refused by assembly name, always has one;
//   - BaseBeyondObject for a type with no base at all: object and interfaces are refused before the base-type rule.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class TransferModelShapeHelperUnitTests
    {
        private static readonly Compilation Compilation = CSharpCompilation.Create("TransferModelShapeHelpers",
            [CSharpSyntaxTree.ParseText("namespace T { public interface IShape { } public class Plain { } public class Derived : Plain { } }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        [Fact]
        public void An_assembly_is_named_by_its_name()
        {
            Assert.Equal("TransferModelShapeHelpers", TransferModelShape.AssemblyDisplayName(Compilation.Assembly));
        }

        [Fact]
        public void No_assembly_is_named_as_another_assembly()
        {
            Assert.Equal("another assembly", TransferModelShape.AssemblyDisplayName(null));
        }

        [Fact]
        public void A_derived_class_answers_its_base()
        {
            Assert.Equal("Plain", TransferModelShape.BaseBeyondObject(Compilation.GetTypeByMetadataName("T.Derived")!)?.Name);
        }

        [Fact]
        public void A_class_deriving_only_from_object_answers_none()
        {
            Assert.Null(TransferModelShape.BaseBeyondObject(Compilation.GetTypeByMetadataName("T.Plain")!));
        }

        [Fact]
        public void An_interface_has_no_base_and_answers_none()
        {
            Assert.Null(TransferModelShape.BaseBeyondObject(Compilation.GetTypeByMetadataName("T.IShape")!));
        }
    }
}
