// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

// The negative half of the combination fuzz: combinations that are essentially INVALID must be CAUGHT,
// with the EXACT expected diagnostic — not silently ignored or silently producing wrong/broken code. Each
// case cites the precise DWARF id; if a combination stops erroring (or errors with a different code) this
// fails, which is how it reveals regressions or newly-introduced silent paths.
#pragma warning disable CA1062
public class FeatureCombinationInvalidTests
{
    public static IEnumerable<object[]> InvalidCombos()
    {
        // ── Class-config × feature that must error (excluded from the compiles-clean matrix) ──
        // strict ImplicitConversions × lossy numeric narrowing.
        yield return Case("strict_narrow", "DWARF038", """
                                                       using DwarfMapper;
                                                       namespace Iz;
                                                       public class S { public long V { get; set; } }
                                                       public class D { public int V { get; set; } }
                                                       [DwarfMapper(ImplicitConversions = false)] public partial class M { public partial D Map(S s); }
                                                       """);
        // strict ImplicitConversions × string parse.
        yield return Case("strict_parse", "DWARF038", """
                                                      using DwarfMapper;
                                                      namespace Iz;
                                                      public class S { public string V { get; set; } = ""; }
                                                      public class D { public int V { get; set; } }
                                                      [DwarfMapper(ImplicitConversions = false)] public partial class M { public partial D Map(S s); }
                                                      """);
        // strict ImplicitConversions × cross-category (int → double).
        yield return Case("strict_crosscat", "DWARF038", """
                                                         using DwarfMapper;
                                                         namespace Iz;
                                                         public class S { public int V { get; set; } }
                                                         public class D { public double V { get; set; } }
                                                         [DwarfMapper(ImplicitConversions = false)] public partial class M { public partial D Map(S s); }
                                                         """);
        // enum by-name × a source member with no same-named target member.
        yield return Case("byname_misaligned_enum", "DWARF015", """
                                                                using DwarfMapper;
                                                                namespace Iz;
                                                                public enum A { X, Y, Z } public enum B { X, Y }
                                                                public class S { public A E { get; set; } }
                                                                public class D { public B E { get; set; } }
                                                                [DwarfMapper(EnumStrategy = EnumStrategy.ByName)] public partial class M { public partial D Map(S s); }
                                                                """);
        // NameConvention.Flexible × a post-normalization collision (two sources reduce to one target).
        yield return Case("flexible_collision", "DWARF048", """
                                                            using DwarfMapper;
                                                            namespace Iz;
                                                            public class S { public string UserName { get; set; } = ""; public string user_name { get; set; } = ""; }
                                                            public class D { public string UserName { get; set; } = ""; }
                                                            [DwarfMapper(NameConvention = NameConvention.Flexible)] public partial class M { public partial D Map(S s); }
                                                            """);

        // ── Member × member / member-with-bad-config invalid combinations ──
        // unflatten target × When (the silently-ignored combo the audit found).
        yield return Case("unflatten_x_when", "DWARF045", """
                                                          using DwarfMapper;
                                                          namespace Iz;
                                                          public class Addr { public string City { get; set; } = ""; }
                                                          public class S { public string City { get; set; } = ""; public int T { get; set; } }
                                                          public class D { public Addr Address { get; set; } = new(); }
                                                          [DwarfMapper] public partial class M {
                                                              [MapProperty(nameof(S.City), "Address.City", When = nameof(Ok))]
                                                              public partial D Map(S s);
                                                              private static bool Ok(S s) => s.T > 0;
                                                          }
                                                          """);
        // unflatten target × NullSubstitute.
        yield return Case("unflatten_x_nullsubst", "DWARF045", """
                                                               using DwarfMapper;
                                                               namespace Iz;
                                                               public class Addr { public string City { get; set; } = ""; }
                                                               public class S { public string City { get; set; } = ""; }
                                                               public class D { public Addr Address { get; set; } = new(); }
                                                               [DwarfMapper] public partial class M {
                                                                   [MapProperty(nameof(S.City), "Address.City", NullSubstitute = "x")]
                                                                   public partial D Map(S s);
                                                               }
                                                               """);
        // MapValue conflicting with a MapProperty on the same target.
        yield return Case("mapvalue_x_mapproperty", "DWARF042", """
                                                                using DwarfMapper;
                                                                namespace Iz;
                                                                public class S { public int Id { get; set; } public string Real { get; set; } = ""; }
                                                                public class D { public int Id { get; set; } public string X { get; set; } = ""; }
                                                                [DwarfMapper] public partial class M {
                                                                    [MapValue(nameof(D.X), "c")]
                                                                    [MapProperty(nameof(S.Real), nameof(D.X))]
                                                                    public partial D Map(S s);
                                                                }
                                                                """);
        // NullSubstitute combined with a Use converter (unsupported together).
        yield return Case("nullsubst_x_converter", "DWARF049", """
                                                               using DwarfMapper;
                                                               #nullable enable
                                                               namespace Iz;
                                                               public class S { public string? V { get; set; } }
                                                               public class D { public int V { get; set; } }
                                                               [DwarfMapper] public partial class M {
                                                                   [MapProperty(nameof(S.V), nameof(D.V), Use = nameof(C), NullSubstitute = 0)]
                                                                   public partial D Map(S s);
                                                                   private static int C(string? v) => v is null ? 0 : v.Length;
                                                               }
                                                               """);
        // When predicate that is not a bool method taking the source.
        yield return Case("when_bad_predicate", "DWARF050", """
                                                            using DwarfMapper;
                                                            namespace Iz;
                                                            public class S { public int V { get; set; } }
                                                            public class D { public int V { get; set; } }
                                                            [DwarfMapper] public partial class M {
                                                                [MapProperty(nameof(S.V), nameof(D.V), When = "Nope")]
                                                                public partial D Map(S s);
                                                            }
                                                            """);
        // Duplicate [MapProperty] for the same destination.
        yield return Case("duplicate_mapproperty", "DWARF011", """
                                                               using DwarfMapper;
                                                               namespace Iz;
                                                               public class S { public int A { get; set; } public int B { get; set; } }
                                                               public class D { public int X { get; set; } }
                                                               [DwarfMapper] public partial class M {
                                                                   [MapProperty(nameof(S.A), nameof(D.X))]
                                                                   [MapProperty(nameof(S.B), nameof(D.X))]
                                                                   public partial D Map(S s);
                                                               }
                                                               """);

        static object[] Case(string label, string id, string src)
        {
            return new object[] { label, id, src };
        }
    }

    [Theory]
    [MemberData(nameof(InvalidCombos))]
    public void Invalid_combination_is_caught_with_exact_diagnostic(string label, string expectedId, string source)
    {
        _ = label;
        var (diags, _) = GeneratorTestHarness.Run(source);
        Assert.True(diags.Any(d => d.Id == expectedId),
            $"Expected diagnostic {expectedId} for invalid combination '{label}', but got: "
            + (diags.Length == 0 ? "(none)" : string.Join(", ", diags.Select(d => d.Id).Distinct())));
    }
}
