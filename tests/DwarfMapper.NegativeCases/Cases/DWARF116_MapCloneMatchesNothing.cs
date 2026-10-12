// SPDX-License-Identifier: GPL-2.0-only
// CASE: [MapClone] on methods that are not same-type create maps - one between two different types, one an
//       update-into - beside a genuine clone, which it accepts in silence.
// WHY:  Round 32, under the owner's 2026-09-27 ruling that no DWARF warning may need a project-wide NoWarn: DWARF076
//       ("source and target are the same type") used to tell the reader to add it to <NoWarn> when the shallow copy was
//       intended. [MapClone] says so for the one method instead. A statement with nothing to accept is not harmless -
//       left on a method that later becomes a same-type map, it would accept a copy-paste slip nobody reviewed - so
//       it is reported, with the reason. An update-into is never a self-map (refreshing an instance from a detached
//       copy is the pattern DWARF076 exempts), so the statement is dead there too.
// EXPECT: DWARF116
// EXPECT-MESSAGE DWARF116: [MapClone] on 'ToDto' accepts a same-type map that is not there: its source 'global::Demo.Order' and destination 'global::Demo.OrderDto' are different types
// EXPECT-MESSAGE DWARF116: [MapClone] on 'Refresh' accepts a same-type map that is not there: it returns nothing, so it is an update-into or no map at all
// EXPECT-CS:
// NOTE: 'Copy' is the statement doing its job - a same-type create map, and no DWARF076 for it.

using DwarfMapper;

namespace Demo;

public class Order
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
}

public class OrderDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
}

[DwarfMapper]
public partial class M
{
    [MapClone]
    public partial Order Copy(Order source);

    [MapClone]
    public partial OrderDto ToDto(Order source);

    [MapClone]
    public partial void Refresh(Order source, Order target);
}
