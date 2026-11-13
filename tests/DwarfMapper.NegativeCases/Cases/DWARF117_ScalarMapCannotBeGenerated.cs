// SPDX-License-Identifier: GPL-2.0-only
// CASE: mapping methods between scalar types that the generator cannot emit as a conversion of the value - two that
//       need no conversion at all and one with a null to handle first - beside one that converts.
// WHY:  Round 35 (T16). A map whose destination is an enum or a built-in scalar is a conversion of the value, emitted
//       as `return helper(value);`. A declared method with such a destination used to be built as an object with no
//       members instead - `return new StatusDto { };`, the default for every input, in a clean build - while
//       [GenerateMap] of the same pair converted. Both convert now. What is left to refuse is the pair with no
//       helper to return through (the value is assigned as it is: the declared method was a bare CS8795, the
//       attribute generated nothing and said nothing) and the pair where a null comes first (it was DWARF007 on
//       HasValue, an id about something else).
// EXPECT: DWARF117, DWARF078
// EXPECT-MESSAGE DWARF117: Mapping method 'Widen' maps 'int' to 'long', which needs no conversion: the value is assigned as it is, so there is nothing to generate
// EXPECT-MESSAGE DWARF117: Mapping method 'Same' maps 'Demo.Status' to 'Demo.Status', which needs no conversion
// EXPECT-MESSAGE DWARF117: Mapping method 'Lifted' maps 'Demo.Status?' to 'Demo.StatusDto?', and a null has to be handled before the value can be converted
// EXPECT-CS: CS8795
// NOTE: 'ToDto' is the map doing its job - enum to enum, by name - and carries no diagnostic.

using DwarfMapper;

namespace Demo;

public enum Status
{
    Open,
    Closed
}

public enum StatusDto
{
    Open,
    Closed
}

[DwarfMapper]
public partial class M
{
    public partial StatusDto ToDto(Status status);

    public partial long Widen(int value);

    public partial Status Same(Status status);

    public partial StatusDto? Lifted(Status? status);
}
