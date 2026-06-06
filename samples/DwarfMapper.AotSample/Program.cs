// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

var mapper = new SampleMapper();
var dto = mapper.ToDto(new Source { Id = 7, Label = "vein" });
System.Console.WriteLine($"{dto.Id}:{dto.Label}");

public class Source { public int Id { get; set; } public string Label { get; set; } = ""; }
public class Target { public int Id { get; set; } public string Label { get; set; } = ""; }

[DwarfMapper]
public partial class SampleMapper
{
    public partial Target ToDto(Source s);
}
