// SPDX-License-Identifier: GPL-2.0-only
#nullable enable

using System;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// [Flags] enums.
//
// The whole point of [Flags] is that `Read | Write` (value 3) is a LEGAL value. The by-name enum converter
// emitted one switch arm per DECLARED member and `_ => throw`, so every combined value — the ordinary case for
// a flags enum — threw ArgumentOutOfRangeException at run time, on perfectly valid input, with nothing said at
// build time. ByName is the DEFAULT strategy, so that was the default behaviour for every [Flags] enum.
//
// The string direction was broken symmetrically: enum -> string produced "Read, Write" (that is what
// Enum.ToString does), and string -> enum then choked on it. The library could not read back a value it had
// itself just written.
//
// These are runtime tests on purpose. A generator test can only assert on emitted text; the claim here is that
// the values actually survive the round trip.

[Flags]
public enum PermSrc
{
    None = 0,
    Read = 1,
    Write = 2,
    Exec = 4,
}

[Flags]
public enum PermDst
{
    None = 0,
    Read = 1,
    Write = 2,
    Exec = 4,
}

public sealed class FlagSrc
{
    public PermSrc Perm { get; set; }
    public PermSrc AsText { get; set; }
}

public sealed class FlagDst
{
    public PermDst Perm { get; set; }
    public string AsText { get; set; } = "";
}

public sealed class FlagTextSrc
{
    public string Perm { get; set; } = "";
}

public sealed class FlagTextDst
{
    public PermDst Perm { get; set; }
}

[DwarfMapper]
public partial class FlagsMapper
{
    public partial FlagDst Map(FlagSrc src);
    public partial FlagTextDst MapFromText(FlagTextSrc src);
}

public class FlagsEnumRuntimeTests
{
    [Theory]
    [InlineData(PermSrc.None, PermDst.None)]
    [InlineData(PermSrc.Read, PermDst.Read)]
    [InlineData(PermSrc.Read | PermSrc.Write, PermDst.Read | PermDst.Write)]
    [InlineData(PermSrc.Read | PermSrc.Write | PermSrc.Exec, PermDst.Read | PermDst.Write | PermDst.Exec)]
    [InlineData(PermSrc.Write | PermSrc.Exec, PermDst.Write | PermDst.Exec)]
    public void Combined_flag_values_map_across(PermSrc input, PermDst expected)
    {
        var result = new FlagsMapper().Map(new FlagSrc { Perm = input });

        Assert.Equal(expected, result.Perm);
    }

    [Fact]
    public void An_undeclared_bit_still_throws_rather_than_being_silently_dropped()
    {
        // 8 is not a declared flag. The bitwise mapper translates the flags it knows and is left with an
        // unmappable bit — which must throw, not quietly vanish. Being permissive here would trade a loud
        // failure for a silent data change, which is the trade this library exists to refuse.
        var src = new FlagSrc { Perm = (PermSrc)(1 | 8) };

        Assert.Throws<ArgumentOutOfRangeException>(() => new FlagsMapper().Map(src));
    }

    [Fact]
    public void A_combined_value_formats_to_a_string_and_reads_back()
    {
        var forward = new FlagsMapper().Map(new FlagSrc { AsText = PermSrc.Read | PermSrc.Exec });

        // This is what Enum.ToString() produces for a combined flags value.
        Assert.Equal("Read, Exec", forward.AsText);

        // ...and the string -> enum direction must accept it. It previously threw on the library's own output.
        var back = new FlagsMapper().MapFromText(new FlagTextSrc { Perm = forward.AsText });

        Assert.Equal(PermDst.Read | PermDst.Exec, back.Perm);
    }

    [Fact]
    public void A_single_name_still_reads_back()
    {
        var back = new FlagsMapper().MapFromText(new FlagTextSrc { Perm = "Write" });

        Assert.Equal(PermDst.Write, back.Perm);
    }

    [Fact]
    public void An_unrecognized_name_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FlagsMapper().MapFromText(new FlagTextSrc { Perm = "Read, Nonsense" }));
    }
}
