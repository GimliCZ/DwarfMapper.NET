// SPDX-License-Identifier: GPL-2.0-only
// CASE: An ordinary mapper, and the silence it must keep
// WHY:  A negative-case suite that can only prove diagnostics FIRE is half an instrument. If the harness ever
//       starts reporting ids that were not asked for — a widened trigger, a leaked Info, a driver
//       misconfiguration — every other case in this project would go on passing while the consumer's build
//       filled with noise. This case fails first, and it is the one that says the others mean something.
// EXPECT: none
// EXPECT-CS:

using System;
using System.Collections.Generic;
using DwarfMapper;

namespace Demo;

public enum Level
{
    Low,
    High
}

public class Order
{
    public Guid Id { get; set; }
    public string Customer { get; set; } = "";
    public Level Priority { get; set; }
    public List<string> Lines { get; set; } = [];
}

public class OrderDto
{
    public Guid Id { get; set; }
    public string Customer { get; set; } = "";
    public Level Priority { get; set; }
    public List<string> Lines { get; set; } = [];
}

[DwarfMapper]
public partial class M
{
    public partial OrderDto Map(Order source);
}
