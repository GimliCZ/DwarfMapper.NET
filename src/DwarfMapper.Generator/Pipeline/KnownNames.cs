// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Pipeline;

/// <summary>
/// Single source of truth for the DwarfMapper attribute / namespace names the generator matches by string.
/// The generator is a netstandard2.0 analyzer and does NOT reference the runtime <c>DwarfMapper</c> assembly,
/// so it cannot use <c>nameof(DwarfMapper.MapPropertyAttribute)</c> — the names have to be literals. Keeping
/// each literal in exactly one place means a rename/typo can't leave one match site out of sync and silently
/// stop detecting an attribute (which would drop the user's mapping config without a diagnostic).
/// <para>Each <c>*Fqn</c> is a compile-time concatenation of <see cref="Ns"/> and the simple name, so it stays
/// in lockstep with the simple name automatically.</para>
/// </summary>
internal static class KnownNames
{
    /// <summary>The runtime attributes' namespace (also the <c>ContainingNamespace.Name</c> checked at match sites).</summary>
    public const string Ns = "DwarfMapper";

    // ── Attribute simple names (matched via AttributeClass.Name) ──
    public const string DwarfMapper          = "DwarfMapperAttribute";
    public const string DwarfMapperOptions   = "DwarfMapperOptionsAttribute";
    public const string DwarfMapperDefaults  = "DwarfMapperDefaultsAttribute";
    public const string MapCollectionKey     = "MapCollectionKeyAttribute";
    public const string GenerateMap          = "GenerateMapAttribute";
    public const string GenerateWrapperMap   = "GenerateWrapperMapAttribute";
    public const string MapProperty          = "MapPropertyAttribute";
    public const string MapIgnore            = "MapIgnoreAttribute";
    public const string MapIgnoreSource      = "MapIgnoreSourceAttribute";
    public const string MapValue             = "MapValueAttribute";
    public const string MapConstructor       = "MapConstructorAttribute";
    public const string BeforeMap            = "BeforeMapAttribute";
    public const string AfterMap             = "AfterMapAttribute";
    public const string ReverseMap           = "ReverseMapAttribute";
    public const string RoundTrip            = "RoundTripAttribute";
    public const string Flatten              = "FlattenAttribute";
    public const string FlattenGraph         = "FlattenGraphAttribute";
    public const string Reinterpret          = "ReinterpretAttribute";
    public const string AutoNest             = "AutoNestAttribute";
    public const string MapDerivedType       = "MapDerivedTypeAttribute";
    public const string DwarfMapperConstructor = "DwarfMapperConstructorAttribute";
    public const string UsesMap              = "UsesMapAttribute";
    public const string ValidationRoot       = "DwarfMapperValidationRootAttribute";
    public const string DwarfProvidesMap     = "DwarfProvidesMapAttribute";
    public const string DwarfRequiresMap     = "DwarfRequiresMapAttribute";
    public const string MapConfig            = "MapConfig";
    // Generic arity-2 metadata name for compilation.GetTypeByMetadataName lookups.
    public const string MapConfigMetadata    = "DwarfMapper.MapConfig`2";

    // ── Fully-qualified names (matched via AttributeClass.ToDisplayString()) ──
    public const string DwarfMapperFqn        = Ns + "." + DwarfMapper;
    public const string DwarfMapperOptionsFqn = Ns + "." + DwarfMapperOptions;
    public const string DwarfMapperDefaultsFqn = Ns + "." + DwarfMapperDefaults;
    public const string MapCollectionKeyFqn   = Ns + "." + MapCollectionKey;
    public const string MapPropertyFqn        = Ns + "." + MapProperty;
    public const string MapIgnoreFqn          = Ns + "." + MapIgnore;
    public const string MapIgnoreSourceFqn    = Ns + "." + MapIgnoreSource;
    public const string MapValueFqn           = Ns + "." + MapValue;
    public const string BeforeMapFqn          = Ns + "." + BeforeMap;
    public const string AfterMapFqn           = Ns + "." + AfterMap;
    public const string ReverseMapFqn         = Ns + "." + ReverseMap;
    public const string RoundTripFqn          = Ns + "." + RoundTrip;
    public const string FlattenFqn            = Ns + "." + Flatten;
    public const string FlattenGraphFqn       = Ns + "." + FlattenGraph;
    public const string ReinterpretFqn        = Ns + "." + Reinterpret;
    public const string AutoNestFqn           = Ns + "." + AutoNest;
    public const string MapDerivedTypeFqn     = Ns + "." + MapDerivedType;
    public const string DwarfMapperConstructorFqn = Ns + "." + DwarfMapperConstructor;
    public const string UsesMapFqn            = Ns + "." + UsesMap;
    public const string ValidationRootFqn     = Ns + "." + ValidationRoot;
    public const string DwarfProvidesMapFqn   = Ns + "." + DwarfProvidesMap;
    public const string DwarfRequiresMapFqn   = Ns + "." + DwarfRequiresMap;
}
