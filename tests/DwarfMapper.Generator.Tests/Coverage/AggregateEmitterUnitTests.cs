// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Model;
using DwarfMapper.Generator.Pipeline;

// Unit tests for AggregateEmitter arms no compilation reaches (per-branch rule: expose and test directly):
//   - IsAmbientUpdateRegisterable's partial and extra-parameter refusals: update-into models are built in one place,
//     always partial and never with extra parameters;
//   - EmitServiceCollection with no mappers: its only caller returns first when no mapper is usable;
//   - WithoutGlobalPrefix on an unqualified name: both of its callers pass global::-qualified names.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class AggregateEmitterUnitTests
    {
        private static readonly MapMethodModel Update = new(
            "Update",
            "public",
            "global::Demo.Dst",
            "global::Demo.Src",
            "s",
            true,
            EquatableArray.From(Array.Empty<MemberMap>()),
            EquatableArray.From(Array.Empty<string>()),
            EquatableArray.From(Array.Empty<HookCall>()),
            false,
            "",
            IsUpdateInto: true,
            ParameterIsPublicType: true,
            ReturnIsPublicType: true);

        [Fact]
        public void A_public_partial_two_parameter_update_into_is_registerable()
        {
            Assert.True(AggregateEmitter.IsAmbientUpdateRegisterable(Update));
        }

        [Fact]
        public void An_update_into_that_is_neither_partial_nor_emitted_as_non_partial_is_not_registerable()
        {
            Assert.False(AggregateEmitter.IsAmbientUpdateRegisterable(Update with { IsPartial = false }));
        }

        [Fact]
        public void An_update_into_emitted_as_non_partial_is_registerable()
        {
            Assert.True(AggregateEmitter.IsAmbientUpdateRegisterable(Update with { IsPartial = false, EmitAsNonPartial = true }));
        }

        [Fact]
        public void An_update_into_with_extra_parameters_is_not_registerable()
        {
            Assert.False(AggregateEmitter.IsAmbientUpdateRegisterable(Update with { ExtraParameters = EquatableArray.From(new[] { "int scale" }) }));
        }

        [Fact]
        public void No_mappers_emit_no_service_collection_registration()
        {
            Assert.Null(AggregateEmitter.EmitServiceCollection(Array.Empty<MapperClassModel>(), "Demo"));
        }

        [Fact]
        public void A_global_prefix_is_removed_and_an_unqualified_name_is_left_as_it_is()
        {
            Assert.Equal("Demo.OrderDto", AggregateEmitter.WithoutGlobalPrefix("global::Demo.OrderDto"));
            Assert.Equal("object", AggregateEmitter.WithoutGlobalPrefix("object"));
        }
    }
}
