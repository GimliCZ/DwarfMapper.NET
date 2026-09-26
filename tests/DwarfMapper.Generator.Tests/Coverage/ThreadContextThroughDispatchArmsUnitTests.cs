// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Model;
using DwarfMapper.Generator.Pipeline;

// Unit tests for MapperExtractor.ThreadContextThroughDispatchArms, widened from private to internal (owner ruling
// 2026-09-13: extract, expose, test — no deletion).
//
// Why no generator fixture reaches its "arm already needs ctx" arm: a [MapDerivedType] arm is resolved with
// TryResolveConversion's needsCtx, which is false for an object pair at that moment — Preserve and SetNull force the
// __DwarfMap_Obj_* helper recursion-capable only AFTER resolution, and this pass is what flags the arm. Probed with the
// Preserve auto-nested arm, the SetNull arm, a declared-method arm and overloaded dispatch names: every arm arrived
// unflagged. The arm stays as the idempotence guard its comment names ("previously patched"), and is pinned here.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ThreadContextThroughDispatchArmsUnitTests
    {
        private static MapMethodModel Dispatch(string name, bool isPartial, params DerivedTypeArm[] arms) =>
            new(name, "public", "global::Demo.AnimalDto", "global::Demo.Animal", "a", true,
                EquatableArray.From(Array.Empty<MemberMap>()),
                EquatableArray.From(Array.Empty<string>()),
                EquatableArray.From(Array.Empty<HookCall>()),
                false,
                "",
                IsPartial: isPartial,
                MaxDepth: 64,
                DerivedTypeArms: EquatableArray.From(arms));

        private static HashSet<string> Names(params string[] names) => new(names, StringComparer.Ordinal);

        private static MapMethodModel Run(MapMethodModel model, HashSet<string>? recursionCapable = null, HashSet<string>? selfRecursive = null)
        {
            var methods = new List<MapMethodModel> { model };
            MapperExtractor.ThreadContextThroughDispatchArms(methods, recursionCapable ?? Names(), selfRecursive ?? Names(), 128);
            return Assert.Single(methods);
        }

        [Fact]
        public void An_arm_already_needing_ctx_is_kept_and_still_makes_the_dispatch_recursion_capable()
        {
            var arm = new DerivedTypeArm("global::Demo.Dog", "__DwarfMap_Obj_Dog", ConverterNeedsDepthCtx: true);

            var result = Run(Dispatch("Map", isPartial: true, arm));

            Assert.True(result.IsRecursionCapable);
            Assert.Equal(128, result.MaxDepth);
            Assert.Equal(arm, Assert.Single(result.DerivedTypeArms));
        }

        [Fact]
        public void A_recursion_capable_synthesized_arm_is_flagged_and_a_synthesized_dispatch_keeps_its_depth()
        {
            var result = Run(Dispatch("__DwarfMap_Obj_Animal", isPartial: false, new DerivedTypeArm("global::Demo.Dog", "__DwarfMap_Obj_Dog")),
                recursionCapable: Names("__DwarfMap_Obj_Dog"));

            Assert.True(result.IsRecursionCapable);
            Assert.Equal(64, result.MaxDepth);
            Assert.True(Assert.Single(result.DerivedTypeArms).ConverterNeedsDepthCtx);
        }

        [Fact]
        public void A_self_recursive_declared_arm_is_redirected_to_its_depth_companion()
        {
            var result = Run(Dispatch("Map", isPartial: true, new DerivedTypeArm("global::Demo.Dog", "ToDog")),
                selfRecursive: Names("ToDog"));

            var arm = Assert.Single(result.DerivedTypeArms);
            Assert.Equal("__DwarfMap_Depth_ToDog", arm.ConverterMethod);
            Assert.True(arm.ConverterNeedsDepthCtx);
        }

        [Fact]
        public void An_arm_needing_nothing_and_a_method_with_no_arms_are_left_untouched()
        {
            var plain = Dispatch("Map", isPartial: true, new DerivedTypeArm("global::Demo.Dog", "ToDog"));
            var noArms = Dispatch("Other", isPartial: true);
            var methods = new List<MapMethodModel> { plain, noArms };

            MapperExtractor.ThreadContextThroughDispatchArms(methods, Names(), Names(), 128);

            Assert.Equal(plain, methods[0]);
            Assert.Equal(noArms, methods[1]);
        }
    }
}
