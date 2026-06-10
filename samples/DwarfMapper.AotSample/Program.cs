// SPDX-License-Identifier: GPL-2.0-only
using System;
using DwarfMapper;

// ── Basic identity mapper (original gate) ─────────────────────────────────────
var mapper = new SampleMapper();
var dto = mapper.ToDto(new Source { Id = 7, Label = "vein" });
Console.WriteLine($"{dto.Id}:{dto.Label}");

// ── Checked integral narrowing: long → int ────────────────────────────────────
// Emits: global::System.Int32.CreateChecked(v)  — AOT-safe static call.
var narrowMapper = new NarrowMapper();

var inRange = narrowMapper.Map(new LongHolder { V = 42L });
Console.WriteLine($"narrow in-range: {inRange.V}"); // 42

try
{
    narrowMapper.Map(new LongHolder { V = (long)int.MaxValue + 1L });
    Console.WriteLine("ERROR: expected OverflowException");
}
catch (OverflowException)
{
    Console.WriteLine("narrow overflow: OverflowException (correct)");
}

// ── string → int via IParsable<int>.Parse ─────────────────────────────────────
// Emits: global::System.Int32.Parse(v, CultureInfo.InvariantCulture)  — AOT-safe.
var strToIntMapper = new StringToIntAotMapper();
var parsed = strToIntMapper.Map(new StringHolder { V = "99" });
Console.WriteLine($"string→int: {parsed.V}"); // 99

try
{
    strToIntMapper.Map(new StringHolder { V = "not-a-number" });
    Console.WriteLine("ERROR: expected FormatException");
}
catch (FormatException)
{
    Console.WriteLine("string→int bad input: FormatException (correct)");
}

// ── string → Guid via IParsable<Guid>.Parse ───────────────────────────────────
var guidStr = "12345678-1234-5678-1234-567812345678";
var strToGuidMapper = new StringToGuidAotMapper();
var guidResult = strToGuidMapper.Map(new StringHolder { V = guidStr });
Console.WriteLine($"string→Guid: {guidResult.V}"); // 12345678-1234-5678-1234-567812345678

// ── int → string via IFormattable.ToString(null, InvariantCulture) ────────────
var intToStrMapper = new IntToStringAotMapper();
var strResult = intToStrMapper.Map(new IntHolder2 { V = -42 });
Console.WriteLine($"int→string: {strResult.V}"); // -42

Console.WriteLine("AOT gate: all checks passed.");

// ── Types ─────────────────────────────────────────────────────────────────────

public class Source { public int Id { get; set; } public string Label { get; set; } = ""; }
public class Target { public int Id { get; set; } public string Label { get; set; } = ""; }

[DwarfMapper]
public partial class SampleMapper
{
    public partial Target ToDto(Source s);
}

public class LongHolder { public long V { get; set; } }
public class IntHolder  { public int  V { get; set; } }

[DwarfMapper]
public partial class NarrowMapper { public partial IntHolder Map(LongHolder s); }

public class StringHolder { public string V { get; set; } = ""; }
public class IntHolder2   { public int    V { get; set; } }
public class GuidHolder   { public Guid   V { get; set; } }
public class StrHolder2   { public string V { get; set; } = ""; }

[DwarfMapper] public partial class StringToIntAotMapper  { public partial IntHolder2 Map(StringHolder s); }
[DwarfMapper] public partial class StringToGuidAotMapper { public partial GuidHolder  Map(StringHolder s); }
[DwarfMapper] public partial class IntToStringAotMapper  { public partial StrHolder2  Map(IntHolder2 s); }
