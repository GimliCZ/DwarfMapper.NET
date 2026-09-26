// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Model;

// Unit tests for MapMethodModel's Emit…TypeSignature properties, which state once the `signature ?? full name` fallback
// that MapEmitter and AggregateEmitter spelled inline at every site writing a user-matched signature (per-branch rule:
// extract and test directly). Async-stream and update-into models always carry their signatures, so at those sites the
// fallback was an outcome no mapper reached. The update-into destination's fallback is reached by no site at all, so
// all six answers are pinned here.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MapMethodModelSignatureUnitTests
    {
        private static readonly MapMethodModel Bare = new(
            "Map",
            "public",
            "global::Demo.Dst",
            "global::Demo.Src",
            "s",
            true,
            EquatableArray.From(Array.Empty<MemberMap>()),
            EquatableArray.From(Array.Empty<string>()),
            EquatableArray.From(Array.Empty<HookCall>()),
            false,
            "");

        [Fact]
        public void Without_declared_signatures_every_slot_falls_back_to_the_full_name()
        {
            Assert.Equal("global::Demo.Src", Bare.EmitParameterTypeSignature);
            Assert.Equal("global::Demo.Dst", Bare.EmitReturnTypeSignature);
            Assert.Equal("global::Demo.Dst", Bare.EmitUpdateTargetTypeSignature);
        }

        [Fact]
        public void Declared_signatures_keep_their_annotations()
        {
            var declared = Bare with
            {
                ParameterTypeSignature = "global::Demo.Src?",
                ReturnTypeSignature = "global::Demo.Dst?",
                UpdateTargetTypeSignature = "global::Demo.Dst?"
            };

            Assert.Equal("global::Demo.Src?", declared.EmitParameterTypeSignature);
            Assert.Equal("global::Demo.Dst?", declared.EmitReturnTypeSignature);
            Assert.Equal("global::Demo.Dst?", declared.EmitUpdateTargetTypeSignature);
        }
    }
}
