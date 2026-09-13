// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for two MapperExtractor.Projection helpers whose remaining arms no projection fixture can reach, widened
// from private to internal for the purpose (owner ruling 2026-09-13: extract, expose, test — no deletion):
//   - IsWideningOrSameWidth: reachable from the surface only through an enum source under EnumStrategy.ByValue, whose
//     underlying types are always integral — so its non-integral refusals (and the IntegralInfo default arm) never
//     run; plain numerics convert implicitly before it and a non-integral target is refused upstream;
//   - FlexibleNameComparer: projection uses it as a dictionary comparer (Equals/GetHashCode with non-null member
//     names), so Compare and both null arms of Equals are never called.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ProjectionHelperUnitTests
    {
        private static readonly Compilation Corlib = CSharpCompilation.Create("ProjectionHelperUnitTests",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        private static ITypeSymbol T(SpecialType type) => Corlib.GetSpecialType(type);

        [Theory]
        [InlineData(SpecialType.System_Int16, SpecialType.System_Int32, true)]    // same signedness, wider
        [InlineData(SpecialType.System_Int32, SpecialType.System_Int32, true)]    // same width
        [InlineData(SpecialType.System_UInt32, SpecialType.System_UInt64, true)]
        [InlineData(SpecialType.System_Int64, SpecialType.System_Int32, false)]   // narrowing
        [InlineData(SpecialType.System_Byte, SpecialType.System_Int16, true)]     // unsigned -> strictly wider signed
        [InlineData(SpecialType.System_UInt16, SpecialType.System_Int16, false)]  // unsigned -> same-width signed
        [InlineData(SpecialType.System_SByte, SpecialType.System_Byte, false)]    // signed -> unsigned never
        [InlineData(SpecialType.System_Int16, SpecialType.System_UInt64, false)]
        [InlineData(SpecialType.System_Double, SpecialType.System_Int64, false)]  // non-integral source
        [InlineData(SpecialType.System_Int32, SpecialType.System_Double, false)]  // non-integral target
        [InlineData(SpecialType.System_String, SpecialType.System_Int32, false)]
        public void IsWideningOrSameWidth_accepts_only_range_containing_integral_casts(SpecialType src, SpecialType tgt, bool expected)
        {
            Assert.Equal(expected, MapperExtractor.IsWideningOrSameWidth(T(src), T(tgt)));
        }

        [Fact]
        public void FlexibleNameComparer_equates_names_that_differ_only_in_underscores_and_case()
        {
            var comparer = MapperExtractor.FlexibleNameComparer.Instance;

            Assert.True(comparer.Equals("user_id", "UserId"));
            Assert.Equal(comparer.GetHashCode("user_id"), comparer.GetHashCode("UserId"));
            Assert.False(comparer.Equals("user_id", "UserName"));
        }

        [Fact]
        public void FlexibleNameComparer_Equals_handles_reference_identity_and_nulls()
        {
            var comparer = MapperExtractor.FlexibleNameComparer.Instance;
            const string name = "Name";

            Assert.True(comparer.Equals(name, name));
            Assert.True(comparer.Equals(null, null));
            Assert.False(comparer.Equals(null, name));
            Assert.False(comparer.Equals(name, null));
        }

        [Fact]
        public void FlexibleNameComparer_Compare_orders_normalized_names_and_sorts_null_first()
        {
            var comparer = MapperExtractor.FlexibleNameComparer.Instance;

            Assert.Equal(0, comparer.Compare("User_Id", "userid"));
            Assert.True(comparer.Compare("alpha", "Beta") < 0);
            Assert.True(comparer.Compare(null, "a") < 0);
            Assert.True(comparer.Compare("a", null) > 0);
            Assert.Equal(0, comparer.Compare(null, null));
        }
    }
}
