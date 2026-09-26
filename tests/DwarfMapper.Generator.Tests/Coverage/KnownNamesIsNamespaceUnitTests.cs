// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// KnownNames.IsNamespace replaces the inline `type.ContainingNamespace?.ToDisplayString() == Name` checks at the type- and
// attribute-match sites (per-branch rule: a branch no compilation reaches is extracted and tested directly). Every named
// type a compilation hands the generator has a containing namespace — the global one included — so the
// null-conditional's null answer never ran at any of them.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class KnownNamesIsNamespaceUnitTests
    {
        private static readonly Compilation Compilation = CSharpCompilation.Create("IsNamespace",
            [],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        [Fact]
        public void No_namespace_matches_nothing()
        {
            Assert.False(KnownNames.IsNamespace(null, "System"));
        }

        [Fact]
        public void A_namespace_matches_its_own_name_and_no_other()
        {
            var system = Compilation.GetSpecialType(SpecialType.System_Object).ContainingNamespace;

            Assert.True(KnownNames.IsNamespace(system, "System"));
            Assert.False(KnownNames.IsNamespace(system, "System.Collections.Generic"));
        }
    }
}
