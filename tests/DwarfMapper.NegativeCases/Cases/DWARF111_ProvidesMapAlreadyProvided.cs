// SPDX-License-Identifier: GPL-2.0-only
// CASE: two mappers in one assembly provide the same ambient pair with [ProvidesMap]
// WHY:  [ProvidesMap] asks for exactly one thing, an ambient registration. When the pair is already registered in
//       the assembly — here by an earlier [ProvidesMap] — the marked method is left out, and it was left out silently.
//       DWARF063 reports the same event across ASSEMBLIES and counts distinct assemblies, so it never fired here.
//       Round 30 recorded the drop as an owner question (98abfc2). A Warning like DWARF063: the author owns both
//       providers, and the second attribute does nothing. Two GENERATED maps of one pair are not reported — mappers
//       differing only in class-level policy are the ordinary reason for two (measured: eight in-repo builds).
// EXPECT: DWARF111
// EXPECT-MESSAGE DWARF111: [ProvidesMap] method 'Demo.SecondProvider.Provide' provides 'Demo.Order' -> 'Demo.OrderDto'
// EXPECT-MESSAGE DWARF111: which [ProvidesMap] method 'Demo.FirstProvider.Provide' already registers, so the method is not registered. Remove all but one.
// EXPECT-CS:
// NOTE: Location-less (reported from the aggregate stage), so the message carries the pair and both providers.

using DwarfMapper;

namespace Demo;

public class Order
{
    public int Id { get; set; }
}

public class OrderDto
{
    public int Id { get; set; }
}

[DwarfMapper]
public partial class FirstProvider
{
    [ProvidesMap]
    public static OrderDto Provide(Order o) => new() { Id = o.Id };
}

[DwarfMapper]
public partial class SecondProvider
{
    [ProvidesMap]
    public static OrderDto Provide(Order o) => new() { Id = o.Id };
}
