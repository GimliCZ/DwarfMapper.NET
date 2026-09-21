// SPDX-License-Identifier: GPL-2.0-only
// CASE: a data-only DTO class mapped as the element of a collection, which could be a readonly record struct
//       (round 29, T2.2)
// WHY:  This is the shape DWARF103 exists for: the mapping SUCCEEDS, allocates one OrderDto per element, and
//       says nothing about the fact that the same members declared as a `readonly record struct` would live
//       inside the array — one allocation for the whole collection instead of one per item. Measured on a
//       four-class DTO tree (Issues/round29/RESEARCH-hardware-mode.md, section 9): 0.30x the time at 1,000
//       elements (3.3x faster) and 0.09x at 100,000 (11x faster). A single flat DTO is 2.2x faster at 1,000
//       and 9-12x faster at 100,000 — smaller at the low end, comparable at scale — with 43 % less memory.
//
//       INFORMATIONAL on purpose, and for a stronger reason than the other performance hints. This one asks
//       for a change of MEANING — a struct has no reference identity, cannot be null, and cannot be mutated
//       through an indexer — so it is a judgement call the consumer has to make. Every one of those hazards
//       surfaces as a COMPILE ERROR rather than a silent behaviour change, which is what makes the suggestion
//       safe to print at all; raising it to a warning would turn that judgement into a build break under
//       TreatWarningsAsErrors, the trap DWARF070 taught this project once already.
//
//       The EXACT expected set is the other half of the assertion. The pair here maps element by element and
//       is complete, so nothing else may appear beside it — in particular no completeness or conversion
//       refusal, which would mean the fixture is red for a reason that has nothing to do with this hint.
//
//       The block-copy clause is CONDITIONAL, and this fixture is the shape that earns it: 'Order' is
//       transfer-model shaped in its own right, so advising that it become a struct is checked rather than
//       assumed, and neither type holds a reference, so an unmanaged pair — the block copy's precondition —
//       is possible at all. It still says "could": layout identity and field-name alignment are the rest of
//       BlittableProof's proof and this diagnostic has not run it. DWARF103_TransferModelElementOversized
//       pins the other side, where the clause must be absent.
//
//       One report, not two, though two members map the same pair: the message names the element PAIR and
//       nothing about the member it was reached through, so the second copy is the same string and drops. An
//       Info repeated per member is the shape consumers suppress wholesale.
// EXPECT: DWARF103
// EXPECT-MESSAGE DWARF103: 'Demo.Order' → 'Demo.OrderDto' allocates one 'Demo.OrderDto' per element
// EXPECT-MESSAGE DWARF103: declared as a readonly record struct
// EXPECT-MESSAGE DWARF103: it is 16 bytes, and the collection becomes one allocation instead of one per element
// EXPECT-MESSAGE DWARF103: 'Demo.Order' is transfer-model shaped too, and neither type holds a reference
// EXPECT-MESSAGE DWARF103: as structs with identical layout and matching field names the pair could take the block copy

using System.Collections.Generic;
using DwarfMapper;

namespace Demo;

public sealed class Order
{
    public long Id { get; set; }

    public int Quantity { get; set; }
}

public sealed class OrderDto
{
    public long Id { get; set; }

    public int Quantity { get; set; }
}

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
