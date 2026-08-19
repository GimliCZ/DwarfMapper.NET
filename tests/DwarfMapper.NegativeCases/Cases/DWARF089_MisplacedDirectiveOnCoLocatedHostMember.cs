// SPDX-License-Identifier: GPL-2.0-only
// CASE: the METHOD-placement overload of [MapProperty] / [MapIgnore], written on a member of a co-located
//       [GenerateMap<S,T>] host
// WHY:  The exact inverse of DWARF088. At a co-located host the mapping is declared BY the annotated type,
//       so a member of the host IS part of the declaration and carries the MEMBER form: the host is the
//       DESTINATION, so [MapProperty("SourceMember")] names where the annotated member is filled from and a
//       bare [MapIgnore] excludes it. The two-name form and the one-name [MapIgnore] belong on a mapping
//       method or a mapper class, where the annotated member does not exist — written here they name a
//       destination that the placement has already named, and until this check existed they were dropped in
//       silence along with every named argument riding on them (surface-matrix finding D20, twenty cells).
// EXPECT: DWARF089
// EXPECT-MESSAGE DWARF089: METHOD-placement overload
// EXPECT-MESSAGE DWARF089: [MapProperty("<source>")]
// EXPECT-MESSAGE DWARF089: THE ANNOTATED MEMBER is what gets excluded
// EXPECT-MESSAGE DWARF089: co-located host 'PersonDto'

using DwarfMapper;

namespace Demo;

public sealed class Person
{
    public string Full { get; set; } = "";

    public int Age { get; set; }
}

[GenerateMap<Person, PersonDto>]
public sealed class PersonDto
{
    [MapProperty("Full", "Full")]
    public string Full { get; set; } = "";

    [MapIgnore("Age")]
    public int Age { get; set; }
}
