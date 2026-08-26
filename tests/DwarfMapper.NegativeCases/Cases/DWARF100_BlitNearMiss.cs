// SPDX-License-Identifier: GPL-2.0-only
// CASE: two structs with identical field names and types, one declaring [StructLayout(LayoutKind.Auto)],
//       mapped array-to-array (round 25, T0-B)
// WHY:  This is the shape DWARF100 exists for: the mapping SUCCEEDS and is completely silent about having
//       taken the element-by-element copy. Every part of the blit proof holds — same field count, same
//       names, same types, same pack — except that Auto layout lets the runtime reorder fields, so the two
//       layouts are not PROVABLY identical and the reinterpret is refused. Correctly refused: a blit whose
//       layout assumption is merely probable is silent memory corruption. The remedy is one attribute,
//       [StructLayout(LayoutKind.Sequential)], and without this diagnostic nothing in the build would say so.
//
//       Auto layout is not a hypothetical. DateTime and DateTimeOffset are declared [StructLayout(Auto)],
//       which is why they must never blit however byte-like they look.
//
//       INFORMATIONAL on purpose. The mapping works; only speed is lost, and the caller may not care.
//       Raising it to a warning would turn a performance hint into a build break under
//       TreatWarningsAsErrors — the trap DWARF070 already taught this project once.
//
//       Note which shape is deliberately NOT used here. A name MISMATCH is also a near-miss, but it already
//       fails loudly as DWARF001 (an Error — the members cannot be mapped at all), so DWARF100 would be
//       redundant noise beside it. The name-mismatch near-miss earns its place only once the names have been
//       reconciled with [MapProperty], and BlittableProofNearMissTests covers that path.
//
//       What is NOT here: the broad reading, "report whenever something looked blittable". That would fire
//       on every ordinary struct-array mapping whose members differ, and an informational diagnostic that
//       common gets suppressed wholesale — which would then hide exactly the cases worth reading. A pair
//       whose field counts or field TYPES differ is silent, and that silence is pinned rather than assumed.
// EXPECT: DWARF100
// EXPECT-MESSAGE DWARF100: nearly layout-identical
// EXPECT-MESSAGE DWARF100: not Sequential

using System.Runtime.InteropServices;
using DwarfMapper;

namespace Demo;

public struct NearMissSrc
{
    public int X;

    public int Y;
}

[StructLayout(LayoutKind.Auto)]
public struct NearMissDst
{
    public int X;

    public int Y;
}

public class NearMissSource
{
    public NearMissSrc[] Data { get; set; } = [];
}

public class NearMissTarget
{
    public NearMissDst[] Data { get; set; } = [];
}

[DwarfMapper]
public partial class NearMissMapper
{
    public partial NearMissTarget Map(NearMissSource s);
}
