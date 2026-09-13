// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage suite for MapperExtractor.Projection.cs's IsWideningOrSameWidth and the reference-to-Nullable<struct>
// lift's unguarded arm.
//
// IsWideningOrSameWidth is reached only by an ENUM source under EnumStrategy.ByValue: a plain numeric pair (byte ->
// int) is an implicit conversion decided before the helper runs, and the default by-name strategy refuses an enum
// before asking. Every existing fixture used an int-backed enum, so the byte / sbyte / ushort / ulong arms of its
// underlying-type switch and its signed -> unsigned refusal had never run.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ProjectionEnumWideningCoverageTests
    {
        private static string ByValue(string types) =>
            "#nullable enable\nusing DwarfMapper;\nusing System.Linq;\nnamespace Demo;\n" + types +
            "\n[DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]\npublic partial class M\n{\n    public partial IQueryable<D> Project(IQueryable<S> src);\n}\n";

        [Fact]
        public void Small_and_wide_underlying_enums_widen_with_an_inline_cast()
        {
            var src = ByValue("""
                              public enum B8 : byte { A, B }
                              public enum S8 : sbyte { A, B }
                              public enum U16 : ushort { A, B }
                              public enum U64 : ulong { A, B }
                              public class S { public B8 A { get; set; } public S8 B { get; set; } public U16 C { get; set; } public U64 E { get; set; } }
                              public class D { public int A { get; set; } public long B { get; set; } public int C { get; set; } public ulong E { get; set; } }
                              """);

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("A = (int)__s.A,", generated, StringComparison.Ordinal);   // unsigned 8 -> signed 32
            Assert.Contains("B = (long)__s.B,", generated, StringComparison.Ordinal);  // signed 8 -> signed 64
            Assert.Contains("C = (int)__s.C,", generated, StringComparison.Ordinal);   // unsigned 16 -> signed 32
            Assert.Contains("E = (ulong)__s.E,", generated, StringComparison.Ordinal); // unsigned 64 -> unsigned 64
        }

        [Fact]
        public void Signed_enum_into_an_unsigned_target_is_refused_as_narrowing()
        {
            var src = ByValue("""
                              public enum Kind { A, B }
                              public class S { public Kind A { get; set; } }
                              public class D { public uint A { get; set; } }
                              """);

            var message = Assert.Single(GeneratorAssert.Reports(src, "DWARF028")).GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("'A'", message, StringComparison.Ordinal);
            Assert.Contains("enum→integral conversion is narrowing", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Non_annotated_reference_source_lifts_into_a_nullable_struct_without_a_null_guard()
        {
            // S1 is not annotated nullable, so it cannot be null and needs no `== null ? null :` guard — guarding it
            // is the false-CS8601 shape ProjectionSourceMayBeNull exists to avoid. The lift to D1? is implicit.
            var src = ByValue("""
                              public class S1 { public int X { get; set; } }
                              public struct D1 { public int X { get; set; } }
                              public class S { public S1 M { get; set; } = new(); }
                              public class D { public D1? M { get; set; } }
                              """);

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("M = new global::Demo.D1 { X = __s.M.X },", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__s.M == null", generated, StringComparison.Ordinal);
        }
    }
}
