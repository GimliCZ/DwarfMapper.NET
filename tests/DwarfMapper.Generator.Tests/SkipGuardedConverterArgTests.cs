// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     A nullable source member mapped through a user-declared converter whose parameter is non-nullable reports
    ///     DWARF070 for the converter's argument - correctly, unless SkipNullSourceMembers / <c>[MapNullSkip]</c> guards
    ///     the member: the emitted <c>if (src.X is not null) dst.X = Conv(src.X);</c> never hands the converter a null.
    ///     It reported anyway, because the converter-argument edge is decided where the conversion is chosen and the
    ///     skip guard only later.
    /// </summary>
    /// <remarks>
    ///     Found 2026-10-11 migrating the owner's FusedChat off its project-wide NoWarns: three [MapNullSkip] maps
    ///     (<c>TtsChatDocument? Tts</c> into <c>ChatTts Tts</c> through <c>ToChatTts</c>, and two more) kept DWARF070
    ///     with NO per-site answer - <c>[MapProperty(AllowNull = true)]</c> was, correctly, reported stale (DWARF116:
    ///     "SkipNullSourceMembers already keeps the destination's value") while the false positive stood beside it.
    ///     The only way out was the project-wide switch the statements exist to retire.
    /// </remarks>
    public class SkipGuardedConverterArgTests
    {
        private static string Source(string mapAttributes)
        {
            return """
                   using DwarfMapper;
                   namespace Demo;
                   public class Child { public int V { get; set; } }
                   public class ChildDto { public int V { get; set; } }
                   public class A { public Child? Child { get; set; } }
                   public class B { public ChildDto Child { get; set; } = new(); }
                   [DwarfMapper] public partial class M
                   {
                   """ + "\n    " + mapAttributes + " public partial B Map(A a);\n" + """
                       public partial ChildDto ToChildDto(Child c);
                   }
                   """;
        }

        private static (ImmutableArray<Diagnostic> Diagnostics, string Generated) Run(string source)
        {
            return GeneratorTestHarness.Run(source, NullableContextOptions.Enable);
        }

        [Fact]
        public void Without_a_skip_guard_the_converter_argument_reports_DWARF070()
        {
            var d = Assert.Single(Run(Source("")).Diagnostics, x => x.Id == "DWARF070");
            Assert.StartsWith("Source member 'Child' is a nullable reference", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("[MapNullSkip]")]
        public void A_skip_guarded_member_reports_no_DWARF070_for_its_converter_argument(string guard)
        {
            var (diagnostics, generated) = Run(Source(guard));

            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF070");
            // The guard is what makes it safe - pinned so the report is never dropped without it.
            Assert.Contains("is not null", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void The_class_level_option_guards_it_the_same_way()
        {
            var source = Source("").Replace("[DwarfMapper] public partial class M", "[DwarfMapper(SkipNullSourceMembers = true)] public partial class M", StringComparison.Ordinal);

            Assert.DoesNotContain(Run(source).Diagnostics, d => d.Id == "DWARF070");
        }

        [Fact]
        public void A_flatten_leaf_is_not_skip_guarded_so_its_converter_argument_keeps_DWARF070()
        {
            // The retraction follows the guard, not the attribute: the skip pass leaves a dotted source path alone,
            // so `a.Holder.Child` reaches the converter unguarded under [MapNullSkip] and the report is true.
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Child { public int V { get; set; } }
                                  public class ChildDto { public int V { get; set; } }
                                  public class Holder { public Child? Child { get; set; } }
                                  public class A { public Holder Holder { get; set; } = new(); }
                                  public class B { public ChildDto HolderChild { get; set; } = new(); }
                                  [DwarfMapper] public partial class M
                                  {
                                      [MapProperty("Holder.Child", "HolderChild")] [MapNullSkip] public partial B Map(A a);
                                      public partial ChildDto ToChildDto(Child c);
                                  }
                                  """;

            var (diagnostics, generated) = Run(source);

            var d = Assert.Single(diagnostics, x => x.Id == "DWARF070");
            Assert.StartsWith("Source member 'Holder.Child' is a nullable reference", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.Contains("HolderChild = ToChildDto(a.Holder.Child!),", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Only_the_guarded_members_own_report_is_taken_back_from_a_window_that_holds_others()
        {
            // The window the retraction scans holds, ahead of the guarded member's report: Code's DWARF107 (another
            // descriptor) and the unguarded flatten leaf's DWARF070 for 'Holder.Child' (another label). The match is
            // descriptor and label alone - MemberName and location could never differ inside the window - so this is
            // what keeps it from taking a neighbour's report.
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Child { public int V { get; set; } }
                                  public class ChildDto { public int V { get; set; } }
                                  public class Holder { public Child? Child { get; set; } }
                                  public class A { public string Code { get; set; } = ""; public Holder Holder { get; set; } = new(); public Child? Child { get; set; } }
                                  public class B { public string Code { get; set; } = ""; public ChildDto HolderChild { get; set; } = new(); public ChildDto Child { get; set; } = new(); }
                                  [DwarfMapper] public partial class M
                                  {
                                      [MapProperty("Code", "Code", Use = nameof(Trimmed))] [MapProperty("Holder.Child", "HolderChild")] [MapNullSkip]
                                      public partial B Map(A a);
                                      public partial ChildDto ToChildDto(Child c);
                                      private static string? Trimmed(string s) => s.Length == 0 ? null : s.Trim();
                                  }
                                  """;

            var (diagnostics, generated) = Run(source);

            var d = Assert.Single(diagnostics, x => x.Id == "DWARF070");
            Assert.StartsWith("Source member 'Holder.Child' is a nullable reference", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.Single(diagnostics, x => x.Id == "DWARF107");
            Assert.Contains("a.Child is not null", generated, StringComparison.Ordinal);
        }
    }
}
