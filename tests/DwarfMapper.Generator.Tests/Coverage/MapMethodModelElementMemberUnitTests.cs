// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Model;

// Unit tests for MapMethodModel.ElementMember, which replaced the `Members.Count > 0 ? Members[0] : null` and the
// `elem?.X ?? default` chains that EmitAsyncStreamMapMethod and EmitSpanMapMethod spelled at every field they read
// (per-branch rule: extract and test directly). Async-stream and span models are always built with exactly one
// element member, so the "no element" answer never arrived through a mapper. Its promise is pinned here: it must read exactly as the
// null-conditionals did.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MapMethodModelElementMemberUnitTests
    {
        private static MapMethodModel Model(params MemberMap[] members) => new(
            "Map",
            "public",
            "global::System.Collections.Generic.IAsyncEnumerable<global::Demo.Dst>",
            "global::System.Collections.Generic.IAsyncEnumerable<global::Demo.Src>",
            "s",
            true,
            EquatableArray.From(members),
            EquatableArray.From(Array.Empty<string>()),
            EquatableArray.From(Array.Empty<HookCall>()),
            false,
            "");

        [Fact]
        public void A_model_with_an_element_member_answers_that_member()
        {
            var element = new MemberMap("", "", ConverterMethod: "Conv", NullHandling: NullHandling.NullableProjectRef, ConverterNeedsDepthCtx: true);

            Assert.Same(element, Model(element).ElementMember);
        }

        [Fact]
        public void A_model_without_one_answers_a_member_with_no_converter_no_null_handling_and_no_flags()
        {
            var none = Model().ElementMember;

            Assert.Null(none.ConverterMethod);
            Assert.Null(none.EmitConverterMethod);
            Assert.Equal(NullHandling.None, none.NullHandling);
            Assert.False(none.ConverterNeedsDepthCtx);
            Assert.False(none.SourceIsNullableRef);
            Assert.False(none.ConverterParamIsNonNullableRef);
            Assert.False(none.ConverterReturnIsNullableRef);
        }
    }
}
