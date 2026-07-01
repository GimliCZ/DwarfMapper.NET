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
; DWARF006 — retired; superseded by DWARF026 (NoMappableConstructor); do not reuse
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
; DWARF019 — retired; superseded by DWARF028 (ProjectionNotTranslatable); do not reuse
DWARF020 | DwarfMapper | Error | No inverse for [RoundTrip]
DWARF021 | DwarfMapper | Error | Ambiguous inverse for [RoundTrip]
DWARF022 | DwarfMapper | Error | Invalid [Reinterpret] target
DWARF023 | DwarfMapper | Error | [AfterMap] value-type target must be passed by ref
DWARF024 | DwarfMapper | Error | Constructor parameter has no mappable source member
DWARF025 | DwarfMapper | Error | Ambiguous constructor
DWARF026 | DwarfMapper | Error | No mappable constructor
DWARF027 | DwarfMapper | Error | Unsupported collection/dictionary target type
DWARF028 | DwarfMapper | Error | Projection member cannot be translated to a database query
; DWARF029 — reserved; do not reuse
DWARF030 | DwarfMapper | Error | Constructor parameter is part of a reference cycle
DWARF031 | DwarfMapper | Error | Mapping nests too deeply
DWARF032 | DwarfMapper | Error | Custom converter can't preserve reference identity
DWARF033 | DwarfMapper | Error | Abstract or interface source type in auto-nested mapping
DWARF034 | DwarfMapper | Error | Invalid [FlattenGraph] configuration
DWARF035 | DwarfMapper | Error | Invalid [MapDerivedType] configuration
DWARF036 | DwarfMapper | Error | Ambiguous [MapDerivedType] dispatch arms
DWARF037 | DwarfMapper | Warning | OnCycle is ignored under ReferenceHandling.Preserve
DWARF038 | DwarfMapper | Info | Implicit type conversion applied (Error when ImplicitConversions = false)
DWARF039 | DwarfMapper | Info | Source member is read by no destination member (RequiredMapping = Both)
DWARF040 | DwarfMapper | Error | Constant [MapValue] is not assignable to the destination
DWARF041 | DwarfMapper | Error | [MapValue(Use=)] provider method is invalid
DWARF042 | DwarfMapper | Error | Conflicting or invalid [MapValue]
DWARF043 | DwarfMapper | Error | [MapProperty] source path segment not found
DWARF044 | DwarfMapper | Warning | [MapProperty] source path traverses a nullable member
DWARF045 | DwarfMapper | Error | Invalid [MapProperty] unflatten target path
DWARF046 | DwarfMapper | Error | Conflicting [MapProperty] unflatten target
DWARF047 | DwarfMapper | Info | Additional mapping parameter is unused
DWARF048 | DwarfMapper | Error | Ambiguous member match under NameConvention.Flexible
DWARF049 | DwarfMapper | Error | Invalid [MapProperty(NullSubstitute=)]
DWARF050 | DwarfMapper | Error | Invalid [MapProperty(When=)] predicate
DWARF051 | DwarfMapper | Warning | [ReverseMap] cannot auto-invert this configuration
DWARF052 | DwarfMapper | Error | [ReverseMap] has no inverse mapping method
DWARF053 | DwarfMapper | Error | Generic mapping methods are not supported
DWARF054 | DwarfMapper | Error | Mapping is not supported on a generic class
DWARF055 | DwarfMapper | Info | Mapper is very large; consider splitting it
DWARF056 | DwarfMapper | Warning | Pair-scoped attribute matches no mapped pair
DWARF057 | DwarfMapper | Error | Generated mapper name collides with an existing type
DWARF058 | DwarfMapper | Info | Convenience extension method was not generated (ambiguous)
DWARF059 | DwarfMapper | Error | Constructor factory method not found
DWARF060 | DwarfMapper | Error | Conflicting map methods from the same source type
DWARF061 | DwarfMapper | Error | Required ambient map is not provided
DWARF062 | DwarfMapper | Info | Mapper not added to the ambient registry
DWARF063 | DwarfMapper | Warning | Ambiguous ambient map provider
DWARF064 | DwarfMapper | Info | [MapValue] shadows an auto-matchable source member
DWARF065 | DwarfMapper | Info | Update-into replaces a nested member instead of merging it
DWARF066 | DwarfMapper | Info | [MapProperty(When=)] can leave a non-nullable member at its default
DWARF067 | DwarfMapper | Error | [GenerateWrapperMap] wrapper is not a single-payload generic
DWARF068 | DwarfMapper | Error | MapConfigUnsupportedExpression
DWARF069 | DwarfMapper | Error | MapConfigConflict
