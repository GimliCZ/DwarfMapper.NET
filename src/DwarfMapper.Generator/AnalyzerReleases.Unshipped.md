; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DWARF001 | DwarfMapper | Error | Destination member is not mapped
DWARF002 | DwarfMapper | Error | Mapper type must be partial
DWARF003 | DwarfMapper | Error | Invalid mapping method signature
; DWARF004 — reserved; do not reuse
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
DWARF019 | DwarfMapper | Error | Member is not projectable
DWARF020 | DwarfMapper | Error | No inverse for [RoundTrip]
DWARF021 | DwarfMapper | Error | Ambiguous inverse for [RoundTrip]
DWARF022 | DwarfMapper | Error | Invalid [Reinterpret] target
DWARF023 | DwarfMapper | Error | [AfterMap] value-type target must be passed by ref
DWARF024 | DwarfMapper | Error | Constructor parameter has no mappable source member
DWARF025 | DwarfMapper | Error | Ambiguous constructor
DWARF026 | DwarfMapper | Error | No mappable constructor
DWARF027 | DwarfMapper | Error | Unsupported collection/dictionary target type
DWARF028 | DwarfMapper | Error | Projection mapping not translatable
; DWARF029 — reserved; do not reuse
DWARF030 | DwarfMapper | Error | Constructor parameter participates in a reference cycle
DWARF031 | DwarfMapper | Error | Generator nesting depth limit exceeded
DWARF032 | DwarfMapper | Error | [MapProperty(Use=)] converter cannot participate in reference-identity tracking
DWARF033 | DwarfMapper | Error | Abstract or interface source type in auto-nested mapping
DWARF034 | DwarfMapper | Error | Invalid [FlattenGraph] configuration
