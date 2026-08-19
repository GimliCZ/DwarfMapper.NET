// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

public class HookTests
{
    [Fact]
    public void BeforeMap_is_called_with_source()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int Age { get; set; } }
                         public class Dst { public int Age { get; set; } }
                         [DwarfMapper]
                         public partial class M
                         {
                             public partial Dst Map(Src s);
                             [BeforeMap] private static void Check(Src s) { }
                         }
                         """;
        var gen = GeneratorAssert.CompilesClean(s);
        Assert.Contains("Check(s);", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterMap_two_param_is_called_with_source_and_target()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int Age { get; set; } }
                         public class Dst { public int Age { get; set; } }
                         [DwarfMapper]
                         public partial class M
                         {
                             public partial Dst Map(Src s);
                             [AfterMap] private static void Finish(Src s, Dst d) { }
                         }
                         """;
        var gen = GeneratorAssert.CompilesClean(s);
        Assert.Contains("__dwarf_target", gen, StringComparison.Ordinal);
        Assert.Contains("Finish(s, __dwarf_target);", gen, StringComparison.Ordinal);
        Assert.Contains("return __dwarf_target;", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterMap_one_param_is_called_with_target()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int Age { get; set; } }
                         public class Dst { public int Age { get; set; } }
                         [DwarfMapper]
                         public partial class M
                         {
                             public partial Dst Map(Src s);
                             [AfterMap] private static void Touch(Dst d) { }
                         }
                         """;
        var (diagnostics, gen) = GeneratorTestHarness.Run(s);
        GeneratorAssert.EmitsCompilableCode(s);
        Assert.Contains("Touch(__dwarf_target);", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_hook_signature_reports_DWARF018()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int Age { get; set; } }
                         public class Dst { public int Age { get; set; } }
                         [DwarfMapper]
                         public partial class M
                         {
                             public partial Dst Map(Src s);
                             [BeforeMap] private static int Bad(Src s) => 0;   // non-void
                         }
                         """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diagnostics, d => d.Id == "DWARF018");
    }

    [Fact]
    public void Hook_typed_object_applies_to_all()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int Age { get; set; } }
                         public class Dst { public int Age { get; set; } }
                         [DwarfMapper]
                         public partial class M
                         {
                             public partial Dst Map(Src s);
                             [BeforeMap] private static void Log(object o) { }
                         }
                         """;
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.Contains("Log(s);", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterMap_byvalue_on_struct_target_reports_DWARF023()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int X { get; set; } }
                         public struct Dst { public int X { get; set; } }
                         [DwarfMapper] public partial class M
                         {
                             public partial Dst Map(Src s);
                             [AfterMap] private static void Fix(Dst d) { }
                         }
                         """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diagnostics, d => d.Id == "DWARF023");
    }

    [Fact]
    public void AfterMap_ref_on_struct_target_emits_ref_call()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int X { get; set; } }
                         public struct Dst { public int X { get; set; } }
                         [DwarfMapper] public partial class M
                         {
                             public partial Dst Map(Src s);
                             [AfterMap] private static void Fix(ref Dst d) { }
                         }
                         """;
        var gen = GeneratorAssert.CompilesClean(s);
        Assert.Contains("Fix(ref __dwarf_target)", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterMap_byvalue_on_class_target_unchanged()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int X { get; set; } }
                         public class Dst { public int X { get; set; } }
                         [DwarfMapper] public partial class M
                         {
                             public partial Dst Map(Src s);
                             [AfterMap] private static void Fix(Dst d) { }
                         }
                         """;
        var (diagnostics, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF023");
        GeneratorAssert.EmitsCompilableCode(s);
        Assert.Contains("Fix(__dwarf_target)", gen, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A legitimate two-parameter <c>[AfterMap]</c> on an UPDATE-INTO mapper — the shape
    ///     <c>DWARF091</c> must not reject, and the one every other two-arg hook test here misses by sitting
    ///     on a create map.
    /// </summary>
    /// <remarks>
    ///     The collision is exact: <c>void Update(Src, Dst)</c> and <c>void Fix(Src, Dst)</c> have the SAME
    ///     signature, and it is the after-hook shape. That is why <c>DWARF091</c> discriminates on
    ///     PARTIAL-ness — a declaration whose implementing part is absent has no body to run — rather than on
    ///     the signature, which cannot tell the mapping method from the hook. Had it filtered on shape, this
    ///     hook would have been refused along with the recursion it was written to stop (finding D16).
    /// </remarks>
    [Fact]
    public void AfterMap_two_param_on_an_update_into_mapper_is_called_and_not_refused()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int X { get; set; } }
                         public class Dst { public int X { get; set; } }
                         [DwarfMapper] public partial class M
                         {
                             public partial void Update(Src s, Dst d);
                             [AfterMap] private static void Fix(Src s, Dst d) { }
                         }
                         """;
        var gen = GeneratorAssert.CompilesClean(s);

        // The hook runs against the caller's own instance — an update-into writes into `d`, so there is no
        // __dwarf_target here as there is on the create-map path.
        Assert.Contains("Fix(s, d);", gen, StringComparison.Ordinal);

        // And the mapping method does NOT call itself. This is the regression guard for D16's worst shape:
        // before DWARF091, `Update(Src, Dst)` was registered as its own after-hook and the emitted body
        // ended in `Update(s, d);` — unconditional infinite recursion that compiled.
        Assert.DoesNotContain("Update(s, d);", gen, StringComparison.Ordinal);
    }
}
