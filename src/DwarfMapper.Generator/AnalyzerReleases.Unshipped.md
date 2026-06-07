; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DWARF001 | DwarfMapper | Error | Destination member is not mapped
DWARF002 | DwarfMapper | Error | Mapper type must be partial
DWARF003 | DwarfMapper | Error | Invalid mapping method signature
DWARF005 | DwarfMapper | Error | No implicit conversion between mapped members
DWARF006 | DwarfMapper | Error | Destination type is not constructible
DWARF007 | DwarfMapper | Error | Destination member is read-only
DWARF008 | DwarfMapper | Error | MapProperty target not found
DWARF009 | DwarfMapper | Error | MapProperty source not found
DWARF010 | DwarfMapper | Error | Ambiguous source member
DWARF011 | DwarfMapper | Error | Duplicate explicit mapping
DWARF012 | DwarfMapper | Error | Conflicting [MapIgnore] and [MapProperty]
DWARF013 | DwarfMapper | Error | Ambiguous conversion method
DWARF014 | DwarfMapper | Error | Conversion method not found
DWARF015 | DwarfMapper | Error | Incomplete enum mapping
DWARF016 | DwarfMapper | Error | Invalid flatten source
DWARF017 | DwarfMapper | Error | Ambiguous flattened member
DWARF018 | DwarfMapper | Error | Invalid mapping hook signature
