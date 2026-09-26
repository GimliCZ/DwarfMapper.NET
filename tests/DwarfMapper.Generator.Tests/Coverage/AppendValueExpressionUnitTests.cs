// SPDX-License-Identifier: GPL-2.0-only

using System.Text;
using DwarfMapper.Generator.Model;
using DwarfMapper.Generator.Pipeline;

// Unit tests for MapEmitter.AppendValueExpression, widened from private to internal (per-branch rule: extract, expose,
// test — no deletion), for two member shapes resolution never produces:
//   - a null-lifting member (NullableProject) with NO converter. Both producers of NullableProject/NullableProjectRef
//     set it only beside a converter, so the "defensive fallback" that assigns the source directly never ran through a
//     mapper;
//   - a converter that is recursion-capable (needs ctx, depth) on a member that ALSO carries unwrapping null handling.
//     Recursion-capable converters take reference members, which reach the lift instead.
// Each answer is pinned beside its reachable twin, so a change to the shape is visible as a change to the text.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class AppendValueExpressionUnitTests
    {
        private static string Emit(MemberMap member, string depthArg = "0")
        {
            var sb = new StringBuilder();
            MapEmitter.AppendValueExpression(sb, member, "src", "ctx", depthArg);
            return sb.ToString();
        }

        [Fact]
        public void A_null_lifting_member_without_a_converter_assigns_the_source_directly()
        {
            Assert.Equal("src.A", Emit(new MemberMap("A", "A", NullHandling: NullHandling.NullableProject)));
        }

        [Fact]
        public void A_null_lifting_member_with_a_converter_lifts_through_the_call()
        {
            Assert.Equal("src.A.HasValue ? Conv(src.A.Value) : null", Emit(new MemberMap("A", "A", ConverterMethod: "Conv", NullHandling: NullHandling.NullableProject)));
        }

        [Fact]
        public void A_recursion_capable_converter_over_an_unwrapped_value_threads_ctx_and_depth()
        {
            var member = new MemberMap("A", "A", ConverterMethod: "Conv", NullHandling: NullHandling.ValueOrDefault, ConverterNeedsDepthCtx: true);

            Assert.Equal("Conv(src.A.GetValueOrDefault(), ctx, depth + 1)", Emit(member, "depth + 1"));
        }

        [Fact]
        public void A_plain_converter_over_an_unwrapped_value_takes_only_the_value()
        {
            var member = new MemberMap("A", "A", ConverterMethod: "Conv", NullHandling: NullHandling.ValueOrDefault);

            Assert.Equal("Conv(src.A.GetValueOrDefault())", Emit(member));
        }
    }
}
