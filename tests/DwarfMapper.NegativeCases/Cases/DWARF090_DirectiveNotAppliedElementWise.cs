// SPDX-License-Identifier: GPL-2.0-only
// CASE: an UNSCOPED [MapIgnore] / [MapProperty] on a span map, which resolves no members of its own
// WHY:  A span map and an async-stream map map the ELEMENT pair through a mapper the generator synthesizes
//       per (source, target) and shares with every route that reaches that pair. It therefore takes its
//       configuration only from directives that NAME the pair. An unscoped directive belongs to the
//       declaration it sits on and never arrives — so before this check existed, the identical text was
//       honoured on this mapper's create-map overload and dropped on its span overload, in silence
//       (surface-matrix findings D1 and D2). The class-level [MapIgnore] here proves the class site behaves
//       the same way as the method site, which is what made D1 four cells rather than two.
//
//       Refused rather than propagated, for the reason DWARF077 gives about the same synthesized mapper:
//       pushing one method's unscoped directive into a shared pair would silently re-configure a nested
//       mapping another method owns. The remedy is the PAIR-SCOPED twin, which does reach the element pair.
// EXPECT: DWARF038, DWARF090
// EXPECT-MESSAGE DWARF090: [MapIgnore("Id")] on this mapper class
// EXPECT-MESSAGE DWARF090: [MapProperty("Id", "Label")] on this mapping method
// EXPECT-MESSAGE DWARF090: [MapIgnore<SpanDst>("Id")]
// EXPECT-MESSAGE DWARF090: [MapProperty<SpanSrc, SpanDst>("Id", "Label")]
// EXPECT-MESSAGE DWARF090: resolves no members itself
//
//       [Reinterpret] is on the same gate and is the one arm with NO pair-scoped twin, so its message ends
//       differently: the remedy is a DECLARED create map, which an element-wise map resolves its element pair
//       through instead of synthesizing one (finding D12). Its silence mattered most here of all — a forced
//       blit is exactly what a caller reaches for when moving elements in bulk, and both element-wise
//       endpoints dropped it and copied the array element by element.
// EXPECT-MESSAGE DWARF090: [Reinterpret("Data")] on this mapping method
// EXPECT-MESSAGE DWARF090: has no pair-scoped form
// EXPECT-MESSAGE DWARF090: the forced blit runs per element through it
//
//       DWARF038 is declared alongside it and is not noise: it IS the consequence of the silence. With the
//       blit dropped, int[] -> uint[] falls back to a per-element numeric conversion, and that conversion is
//       what raises DWARF038. Remove [Reinterpret] and the warning stays; declare the create map the message
//       names and both the conversion and the warning go away.

using System;
using DwarfMapper;

namespace Demo;

public sealed class SpanSrc
{
    public int Id { get; set; }

    public string? Label { get; set; }
}

public sealed class SpanDst
{
    public int Id { get; set; }

    public string? Label { get; set; }
}

public sealed class BlitSrc
{
    public int Id { get; set; }

    public int[] Data { get; set; } = Array.Empty<int>();
}

public sealed class BlitDst
{
    public int Id { get; set; }

    public uint[] Data { get; set; } = Array.Empty<uint>();
}

[DwarfMapper]
[MapIgnore("Id")]
public partial class SpanElementDirectiveMapper
{
    [MapProperty("Id", "Label")]
    public partial void MapSpan(ReadOnlySpan<SpanSrc> src, Span<SpanDst> dst);

    [Reinterpret("Data")]
    public partial void MapBlits(ReadOnlySpan<BlitSrc> src, Span<BlitDst> dst);
}
