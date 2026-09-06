// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Golden
{
    /// <summary>
    ///     Proves each feature case EXERCISES the feature it is named for. A case that silently generates nothing
    ///     still yields a valid fingerprint, so once Task 6 pins it the gap becomes permanent and invisible — the
    ///     corpus would advertise coverage it does not have.
    /// </summary>
    public class GoldenFeatureCoverageTests
    {
        /// <summary>featureId -> a marker that can only appear if the feature actually fired.</summary>
        public static TheoryData<string, string> FeatureMarkers()
        {
            return new TheoryData<string, string>
            {
                {
                    "Basic", "X = a.X"
                },
                {
                    "UpdateInto", "void Update("
                },
                {
                    "Projection", "global::System.Linq.Queryable.Select("
                },
                {
                    "SpanMap", "for (int __i = 0; __i < src.Length; __i++)"
                },
                {
                    "SpanMapBlit", "MemoryMarshal.Cast"
                },
                {
                    "AsyncStream", "await foreach"
                },
                {
                    "FlattenGraph", "__DwarfMap_FlattenGraph"
                },
                {
                    // Deliberately NOT __DwarfMap_FlattenGraph, which the homogeneous case also emits: the
                    // per-node DISPATCH helper is synthesized only when several concrete node types share a
                    // base, so it cannot appear unless the heterogeneous path actually ran. A marker both cases
                    // satisfy would advertise coverage this case does not add.
                    "HeteroFlattenGraph", "__DwarfMap_FlatNodeDispatch_"
                },
                {
                    "Flatten", "City = "
                },
                {
                    "ConstructorMapping", "new global::Demo.B("
                },
                {
                    "EnumByName", "Unmapped enum value"
                },
                {
                    "EnumByValue", ".CreateChecked("
                },
                {
                    "FlagsEnumFromString", "MemoryExtensions"
                },
                {
                    "NullStrategyThrow", "throw"
                },
                {
                    "NullStrategySetDefault", ".GetValueOrDefault()"
                },
                {
                    "PreserveReferences", "TryGetReference"
                },
                {
                    "DerivedTypes", "ADerived __s =>"
                },
                {
                    "Hooks", "After(a, __dwarf_target);"
                },
                {
                    "ReverseMap", "public void VerifyRoundTrip_ToB"
                },
                {
                    "CoLocatedGenerateMap", "partial class BMapper"
                },
                {
                    "RegistryBasic", "ToDto"
                },
                {
                    "RegistryCollection", "__DwarfMapColl_"
                },
                {
                    "RegistryNested", "__DwarfMapObj_"
                },
                {
                    "RegistryInheritedDestination", "Id = source.Id"
                },
                {
                    "RegistryInheritedSource", "Id = source.Id"
                },
                {
                    // The lift the payload edge was missing. Deliberately the FORGIVING spelling: the plain
                    // `is null ? null :` arm is also reachable from several already-pinned shapes, whereas
                    // `is null ? null! :` can only be emitted by the arm where both ends are non-nullable-
                    // annotated and the converter is a user-declared map — the one this case exists for.
                    "NestedViaDeclaredMap", "is null ? null! : ToDto("
                },
                {
                    "WrapperMapPayloadEdge", "value: src.Value is null ? null : ToDto(src.Value)"
                },
                {
                    // The extra parameter read as a source ACCESS rather than handed over as a finished
                    // expression. `lifted is null ? null :` can only be emitted when the null-handling decision
                    // reaches the Phase 5 member — the whole of task 2.7's second defect — and `lifted` is a
                    // parameter name, so no source-member edge can produce this text by accident.
                    "NullableExtraParameter", "Lifted = lifted is null ? null : ToDto(lifted)"
                },
                {
                    // The '?' surviving into the implementing half of the user's partial. `Map(global::Demo.A? a)`
                    // cannot be produced by the annotation-stripping format that site used before, and the pair's
                    // typeof registration in the same run proves the OTHER string stayed unannotated.
                    "NullableSourceParameter", "Map(global::Demo.A? a)"
                },
                {
                    // The '?' surviving into the RETURN slot of the user's partial. The generic form is the one
                    // the compiler is loud about (CS8819 + CS8619), and `List<global::Demo.B?>` cannot be
                    // produced by the annotation-stripping format that slot used before task 2.8; the `new
                    // global::Demo.B` in the same file proves the identity string kept its own spelling.
                    "NullableReturnType", "public partial global::System.Collections.Generic.List<global::Demo.B?> Many("
                },
                {
                    // The async-stream element edge reading its null decision. The lift with the destination
                    // element cast is what the shared CollectionConverter.ElementExpr writes and what the
                    // hand-written `yield return Conv(__item)` could not produce at all.
                    "AsyncStreamNullableElement", "yield return (__item is null ? null : (global::Demo.ChildDto?)ToDto(__item))"
                },
                {
                    // The COLLECTION element forgiving a user-declared converter's argument. `ToDto(__item!)`
                    // could not be emitted before task 2.9 at all: the '!' was gated on IsSynthesized, which is
                    // false for a method the user declared, and no synthesized helper is ever spelled `ToDto`.
                    // The dictionary value in the same case proves the twin builder answers identically.
                    "ElementViaDeclaredMap", "ToDto(__item!)"
                },
                {
                    // The CALL's result forgiven, which nothing before task 2.9 could emit: the '!' after the
                    // closing paren exists only on the return-side arm, and the `Free` member in the same case
                    // (a nullable destination) proves it is not sprayed on every call.
                    "NullableReturnConverter", "Strict = a.Strict is null ? null! : ToDto(a.Strict)!"
                }
            };
        }

        [Theory]
        [MemberData(nameof(FeatureMarkers))]
        public void Feature_case_generates_output_that_proves_the_feature_fired(string featureId, string marker)
        {
            var c = GoldenCorpus.Cases().Single(x => x.Id == "feat:" + featureId);
            var generator = GeneratorRegistry.All.Single(g => g.Name == c.GeneratorName);
            var run = GeneratorRunner.Run(generator.Create(), c.Source);
            var output = run.AllOutputsConcatenated;

            Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.False(string.IsNullOrWhiteSpace(output),
                $"Feature case '{featureId}' generated NOTHING. Pinned into the manifest it would pass forever " + "while covering nothing.");
            Assert.True(output.Contains(marker, StringComparison.Ordinal),
                $"Feature case '{featureId}' generated output that does not contain '{marker}', so the feature " + "did not fire. Fix the CASE SOURCE (check the attribute/signature shape against the existing " + "dedicated tests for that feature) — do not delete this assertion.\n\n--- generated ---\n" + output);
        }

        [Fact]
        public void Every_feature_case_in_the_corpus_has_a_marker()
        {
            // Otherwise a new feature case could be added and never proved to fire.
            var corpusIds = GoldenCorpus.Cases()
                .Where(c => c.Id.StartsWith("feat:", StringComparison.Ordinal))
                .Select(c => c.Id["feat:".Length..])
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            var markered = FeatureMarkers().Select(row => (string)row[0])
                .OrderBy(x => x, StringComparer.Ordinal).ToList();

            Assert.True(corpusIds.SequenceEqual(markered, StringComparer.Ordinal),
                "Feature cases and markers are out of sync.\n  corpus:  " + string.Join(", ", corpusIds) + "\n  markers: " + string.Join(", ", markered));
        }
    }
}
