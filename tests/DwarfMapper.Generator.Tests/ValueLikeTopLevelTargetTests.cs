// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     A declared top-level pair whose TARGET is a value must CONVERT, not construct.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The enum case shipped broken: <c>[GenerateMap&lt;SrcEnum, DstEnum&gt;]</c> emitted
    ///         <c>return new DstEnum { };</c> — an empty object initializer over an enum, which compiles, has no
    ///         members to flag, and silently returns the zero value while discarding the source. Green build, no
    ///         diagnostic, every mapped value wrong. Fixed in Round 18.
    ///     </para>
    ///     <para>
    ///         The bug CLASS is broader than enums: any target that is a VALUE rather than an object to build.
    ///         This audit walks the rest of that class so the fix cannot turn out to have been a one-off. Each
    ///         case asserts either a correct conversion or an explicit refusal — never silent construction.
    ///     </para>
    /// </remarks>
    public class ValueLikeTopLevelTargetTests
    {
        /// <summary>
        ///     The signature of the bug: an object initializer over a type that has nothing to initialize.
        /// </summary>
        private static void AssertDoesNotObjectConstruct(string generated, string targetType)
        {
            Assert.DoesNotContain($"new {targetType}\n", generated, StringComparison.Ordinal);
            Assert.DoesNotContain($"new {targetType} {{", generated, StringComparison.Ordinal);
            Assert.DoesNotContain($"new {targetType}()", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void An_enum_target_converts_rather_than_constructs()
        {
            // The original defect, kept here beside its siblings so the class is visible in one place.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public enum SrcKind { A, B }
                               public enum DstKind { A, B }

                               [DwarfMapper]
                               [GenerateMap<SrcKind, DstKind>]
                               public partial class M { }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            AssertDoesNotObjectConstruct(generated, "global::Demo.DstKind");
            Assert.Contains("src", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_string_target_converts_rather_than_constructs()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public enum SrcKind { A, B }

                               [DwarfMapper]
                               [GenerateMap<SrcKind, string>]
                               public partial class M { }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            AssertDoesNotObjectConstruct(generated, "string");
            AssertDoesNotObjectConstruct(generated, "global::System.String");
        }

        [Fact]
        public void A_primitive_target_converts_rather_than_constructs()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;

                               [DwarfMapper]
                               [GenerateMap<int, long>]
                               public partial class M { }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            AssertDoesNotObjectConstruct(generated, "long");
        }

        [Fact]
        public void A_Guid_target_is_not_silently_object_constructed()
        {
            // Guid is the sharpest of the struct cases: `new Guid()` compiles, has no members to flag, and
            // produces Guid.Empty — indistinguishable at a glance from a real value, and catastrophic as an id.
            const string src = """
                               using System;
                               using DwarfMapper;
                               namespace Demo;

                               [DwarfMapper]
                               [GenerateMap<string, Guid>]
                               public partial class M { }
                               """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(src);

            // Either it converts, or it refuses. What it must never do is hand back Guid.Empty from an empty
            // initializer while reporting nothing.
            var refused = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
            if (refused)
            {
                return;
            }

            AssertDoesNotObjectConstruct(generated, "global::System.Guid");
        }

        [Fact]
        public void A_DateTimeOffset_target_is_not_silently_object_constructed()
        {
            const string src = """
                               using System;
                               using DwarfMapper;
                               namespace Demo;

                               [DwarfMapper]
                               [GenerateMap<string, DateTimeOffset>]
                               public partial class M { }
                               """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(src);

            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                return;
            }

            AssertDoesNotObjectConstruct(generated, "global::System.DateTimeOffset");
        }

        [Fact]
        public void A_nullable_value_target_is_not_silently_object_constructed()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public enum SrcKind { A, B }
                               public enum DstKind { A, B }

                               [DwarfMapper]
                               [GenerateMap<SrcKind, DstKind?>]
                               public partial class M { }
                               """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(src);

            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                return;
            }

            AssertDoesNotObjectConstruct(generated, "global::Demo.DstKind");
        }

        [Fact]
        public void A_member_less_struct_target_is_refused_rather_than_returned_empty()
        {
            // A struct with no settable members has nothing an object initializer could fill, so constructing one
            // can only ever produce default(T). If the generator cannot convert, it must SAY so.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public struct Money { public Money(int v) { Value = v; } public int Value { get; } }
                               public class Src { public int Value { get; set; } }

                               [DwarfMapper]
                               [GenerateMap<Src, Money>]
                               public partial class M { }
                               """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(src);

            var refused = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

            Assert.True(refused || generated.Contains("Value =", StringComparison.Ordinal) || generated.Contains("new global::Demo.Money(", StringComparison.Ordinal),
                "A member-less struct target was neither refused nor genuinely populated — which means it was " + "constructed empty and handed back as if it held the source's value.\n\n" + generated);
        }
    }
}
