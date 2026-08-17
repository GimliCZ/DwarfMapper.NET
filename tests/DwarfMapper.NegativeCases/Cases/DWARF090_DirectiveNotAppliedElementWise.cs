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
// EXPECT: DWARF090
// EXPECT-MESSAGE DWARF090: [MapIgnore("Id")] on this mapper class
// EXPECT-MESSAGE DWARF090: [MapProperty("Id", "Label")] on this mapping method
// EXPECT-MESSAGE DWARF090: [MapIgnore<SpanDst>("Id")]
// EXPECT-MESSAGE DWARF090: [MapProperty<SpanSrc, SpanDst>("Id", "Label")]
// EXPECT-MESSAGE DWARF090: resolves no members itself

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

[DwarfMapper]
[MapIgnore("Id")]
public partial class SpanElementDirectiveMapper
{
    [MapProperty("Id", "Label")]
    public partial void MapSpan(ReadOnlySpan<SpanSrc> src, Span<SpanDst> dst);
}
