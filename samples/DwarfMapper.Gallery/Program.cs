// SPDX-License-Identifier: GPL-2.0-only

// DwarfMapper Gallery — a progression of mapping examples, simplest first.
// Each NN_*.cs file is a self-contained, annotated example. Run this project to execute them all:
//   dotnet run --project samples/DwarfMapper.Gallery

using DwarfMapper.Gallery.Ex01;

Console.WriteLine("=== DwarfMapper Gallery — simple → advanced ===");
Console.WriteLine();

Example.Run(); // flat map
DwarfMapper.Gallery.Ex02.Example.Run(); // rename
DwarfMapper.Gallery.Ex03.Example.Run(); // built-in type conversions
DwarfMapper.Gallery.Ex04.Example.Run(); // nested objects (auto-nesting)
DwarfMapper.Gallery.Ex05.Example.Run(); // collections & arrays
DwarfMapper.Gallery.Ex06.Example.Run(); // deep dotted paths  ("lambda territory")
DwarfMapper.Gallery.Ex07.Example.Run(); // flatten
DwarfMapper.Gallery.Ex08.Example.Run(); // custom conversion via Use= method
DwarfMapper.Gallery.Ex09.Example.Run(); // When / [MapValue] / NullSubstitute
DwarfMapper.Gallery.Ex10.Example.Run(); // immutable record target
DwarfMapper.Gallery.Ex11.Example.Run(); // IQueryable projection (generated Select lambda)
DwarfMapper.Gallery.Ex12.Example.Run(); // extension method + DI
DwarfMapper.Gallery.Ex13.Example.Run(); // nested/collection-element config on the class
DwarfMapper.Gallery.Ex14.Example.Run(); // same, low-ceremony: [GenerateMap] + extension method
DwarfMapper.Gallery.Ex15.Example.Run(); // co-located: the mapping lives ON the DTO class, no central mapper

Console.WriteLine();
Console.WriteLine("=== done — open each NN_*.cs file for the annotated source ===");
