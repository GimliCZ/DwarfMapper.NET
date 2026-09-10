// SPDX-License-Identifier: GPL-2.0-only
// CASE: the DWARF103 shape with the remedy its own message names — the destination element declared as a
//       readonly record struct — which must go silent
// REMEDY-FOR: DWARF103
// WHY:  Second half of the DWARF103 pair. Identical to DWARF103_TransferModelElement except for the one edit
//       the message asks for: 'OrderDto' is a `readonly record struct` rather than a sealed class. Same
//       members, same names, same mapping — and nothing left to report, because the allocation the hint is
//       about is gone.
//
//       `EXPECT: none` is the whole assertion, and it pins both directions at once. The remedy has to
//       actually silence the diagnostic (a hint whose own fix does not work is worse than no hint), and the
//       set being EXACT means it must not silence it by breaking something else: a remedy that dropped a
//       member from the mapping, or that the constructor selector could not satisfy, would surface here as
//       another id rather than as silence.
//
//       It also pins the far side of the "already a value type" refusal at the report site rather than only
//       in the classifier: a site that asked about size before it asked about kind would name this one.
// EXPECT: none

using System.Collections.Generic;
using DwarfMapper;

namespace Demo;

public sealed class Order
{
    public long Id { get; set; }

    public int Quantity { get; set; }
}

public readonly record struct OrderDto(long Id, int Quantity);

public class Source
{
    public List<Order> Rows { get; set; } = [];

    public Order[] Archive { get; set; } = [];
}

public class Target
{
    public List<OrderDto> Rows { get; set; } = [];

    public OrderDto[] Archive { get; set; } = [];
}

[DwarfMapper]
public partial class TransferModelMapper
{
    public partial Target Map(Source s);
}
