// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     The mapper-wide, read-only facts a resolution runs under: the compilation, the enum and null policies, and
        ///     the option flags (<see cref="MapperOptions" />). Round 31 T08, under the owner's ruling to extend
        ///     ae9c7ea's bundles rather than build one context object: this is the POLICY half of that split, and it is
        ///     what TryResolveConversion, ResolveUnflattenTarget and ResolveConstructorArguments used to take as eight to
        ///     ten loose parameters at 24 call sites — five same-typed bools among them, where a transposed pair
        ///     compiles clean.
        /// </summary>
        /// <remarks>
        ///     A value type: building one per call costs nothing, and <c>with</c> expresses a deliberate deviation at the
        ///     call site (<see cref="WithoutReferenceTracking" />, <see cref="ForElementEndpoint" />) instead of an
        ///     omitted optional argument silently taking its default — which is how two sites came to ignore
        ///     <c>ImplicitConversions</c> unnoticed.
        /// </remarks>
        private readonly record struct ResolutionSettings(
            Compilation Compilation,
            EnumPolicy EnumPolicy,
            NullStrategy NullStrategy,
            MapperOptions Options)
        {
            /// <summary>Preserve/SetNull off: the nullable-underlying recursions resolve a VALUE-typed pair.</summary>
            public ResolutionSettings WithoutReferenceTracking()
            {
                return this with { Options = Options with { IsPreserve = false, IsSetNull = false } };
            }

            /// <summary>
            ///     An ELEMENT resolution at the span-map or async-stream endpoint: the mapper's ImplicitConversions (and
            ///     auto-nest) apply, while NullAsNull, Preserve and SetNull stay off - those loops thread no reference
            ///     context and build no nested collection of their own. Until round 31 these call sites passed every
            ///     optional flag at its DEFAULT, so ImplicitConversions was always true there and a lossy element went
            ///     through strict mode silently (ElementEndpointStrictConversionTests).
            /// </summary>
            public ResolutionSettings ForElementEndpoint()
            {
                return this with { Options = Options with { NullAsNull = false, IsPreserve = false, IsSetNull = false } };
            }
        }

        /// <summary>The mapper policy's settings for one resolution, with the method- or pair-level auto-nest decision.</summary>
        private static ResolutionSettings PolicySettings(MapperPolicy policy, Compilation compilation, bool autoNest)
        {
            return new ResolutionSettings(compilation,
                policy.EnumPolicy,
                policy.NullStrategy,
                new MapperOptions(
                    CaseInsensitive: policy.CaseInsensitive,
                    AutoNest: autoNest,
                    NullAsNull: policy.NullCollections == NullCollectionsBehavior.AsNull,
                    IsPreserve: policy.IsPreserveMode,
                    IsSetNull: policy.IsSetNullMode,
                    ImplicitConversions: policy.ImplicitConversions,
                    NameConvention: policy.NameConvention,
                    SkipNullSourceMembers: policy.SkipNullSrc,
                    AllowNonPublic: policy.AllowNonPublic,
                    ExplicitOnly: policy.ExplicitOnly,
                    IgnoreObsolete: policy.IgnoreObsolete));
        }
    }
}
