// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     ISSUE-021 — the emitted string → <c>[Flags]</c> parser must not allocate.
///     <para>
///     It used to be <c>foreach (var __part in v.Split(','))</c> with <c>__part.Trim()</c>: a string[] plus N
///     substrings on every call, and another string per part that actually needed trimming. Its own doc comment
///     called that "allocation-light". It is now a <c>ReadOnlySpan&lt;char&gt;</c> slice loop, which allocates
///     nothing. <see cref="DwarfMapper.IntegrationTests" /> covers the behaviour; this pins the shape, because
///     a future edit could silently reintroduce Split and no behavioural test would notice.
///     </para>
/// </summary>
public class FlagsParseAllocationTests
{
    private const string FlagsStringSource = """
                                             using DwarfMapper;
                                             using System;
                                             namespace Demo;
                                             [Flags] public enum Perm { None = 0, Read = 1, Write = 2 }
                                             public class A { public string P { get; set; } = ""; }
                                             public class B { public Perm P { get; set; } }
                                             [DwarfMapper(EnumStrategy = EnumStrategy.ByName)]
                                             public partial class M { public partial B Map(A a); }
                                             """;

    [Fact]
    public void Flags_string_parse_emits_no_Split_and_no_Trim_allocation()
    {
        var generated = GeneratorAssert.CompilesClean(FlagsStringSource);

        Assert.DoesNotContain(".Split(", generated, StringComparison.Ordinal);
        Assert.Contains("MemoryExtensions.AsSpan(v)", generated, StringComparison.Ordinal);
        Assert.Contains("__s.Slice(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Flags_string_parse_still_throws_on_an_unknown_name()
    {
        // The allocation rewrite must not quietly turn an unrecognised name into a silent no-op.
        var generated = GeneratorAssert.CompilesClean(FlagsStringSource);

        Assert.Contains("ArgumentOutOfRangeException", generated, StringComparison.Ordinal);
    }
}
