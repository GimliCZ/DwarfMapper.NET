// SPDX-License-Identifier: GPL-2.0-only
// CASE: [GenerateMap] in its co-located form, written on a record.
// WHY:  Round 35 (T22). Every pipeline of the generator reads CLASS declarations, so a record carrying
//       [DwarfMapper] or [GenerateMap] was passed over without a diagnostic. For a record with a co-located
//       [GenerateMap] that meant no maps and no word about it: the build stayed green and the map was simply
//       not there. (A record with [DwarfMapper] failed too, but with the compiler's CS8795 alone.) It is reported
//       where it is written; the map belongs on a partial class, and a record remains a fine source or destination.
// EXPECT: DWARF120
// EXPECT-MESSAGE DWARF120: 'OrderDto' is a record, and [GenerateMap] is read on classes only: nothing is generated for it.

using DwarfMapper;

namespace Demo;

public class Order
{
    public int Id { get; set; }
}

[GenerateMap<Order, OrderDto>]
public record OrderDto(int Id);
