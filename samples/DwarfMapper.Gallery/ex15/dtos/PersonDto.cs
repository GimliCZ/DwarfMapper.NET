// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Gallery.Ex15.Models;

namespace DwarfMapper.Gallery.Ex15.Dtos;

// Co-located mapping with NO partial and NO [DwarfMapper] on the DTO — just declarative attributes.
//   [GenerateMap<Person, PersonDto>] declares the pair (this DTO is its own target)
//   [MapProperty<Person, PersonDto>] carries the Name -> FullName rename
// The generator emits the actual mapper into a SEPARATE generated type (PersonDtoMapper), reachable via the
// generated model.ToPersonDto() extension (and registrable through AddDwarfMappers()); this
// stays a plain sealed data class. Delete this file and the mapping goes with it. Trade-off: PersonDto
// references Person (the host takes the dependency, by design).
[GenerateMap<Person, PersonDto>]
[MapProperty<Person, PersonDto>(nameof(Person.Name), nameof(FullName))]
public sealed class PersonDto
{
    public string FullName { get; set; } = "";
    public int Age { get; set; }
}
