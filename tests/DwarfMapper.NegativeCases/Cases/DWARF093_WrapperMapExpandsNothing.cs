// SPDX-License-Identifier: GPL-2.0-only
// CASE: [GenerateWrapperMap] on a mapper class that declares no [GenerateMap] pair
// WHY:  The attribute is an EXPANSION of the [GenerateMap<A, B>] pair list — for every pair declared on the
//       class it appends the closed wrapper instantiation W<A> -> W<B>. On a class that declares none there
//       is nothing to expand, and ExpandWrapperMaps returned early on the empty list BEFORE any validation
//       ran: no wrapper map and no refusal, at every one of the five mapper endpoints (surface-matrix
//       finding D15).
//
//       Refused rather than widened to cover partial mapping methods, argued from what the attribute means:
//       it is defined relative to [GenerateMap], a partial method is a different declaration mechanism with a
//       different signature, and four of the five endpoints are not create maps at all — an update-into, a
//       projection, a span map and an async-stream map have no W<A> -> W<B> shape to synthesize.
//
//       Reported BEFORE the wrapper's shape is checked. With no pairs to expand even a well-formed
//       Envelope<T> expands nothing, so DWARF067 would send the caller to fix something that changes no
//       output — and DWARF067 is an Error, which would strand every partial mapping method on this class
//       behind CS8795. The wrapper here IS well-formed, which is exactly what makes that ordering visible:
//       DWARF067 must NOT appear.
// EXPECT: DWARF093
// EXPECT-MESSAGE DWARF093: [GenerateWrapperMap(typeof(WrapEnvelope<>))] on 'WrapperFamilyMapper' expands nothing
// EXPECT-MESSAGE DWARF093: this class declares none
// EXPECT-MESSAGE DWARF093: Declare the payload pair as [GenerateMap<A, B>] on this class
// EXPECT-MESSAGE DWARF093: it is refused as DWARF094

using DwarfMapper;

namespace Demo;

public sealed class WrapEnvelope<T>
{
    public T Payload { get; set; } = default!;

    public int Status { get; set; }
}

public sealed class WrapSrc
{
    public int Id { get; set; }
}

public sealed class WrapDst
{
    public int Id { get; set; }
}

[DwarfMapper]
[GenerateWrapperMap(typeof(WrapEnvelope<>))]
public partial class WrapperFamilyMapper
{
    public partial WrapDst Map(WrapSrc src);
}
