// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Gallery.Ex15.Models;

// A plain model POCO with no mapping attributes. The Person -> PersonDto mapping is co-located on the DTO
// (see dtos/PersonDto.cs), so the model stays free of any reference to the DTO.
public sealed class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}
