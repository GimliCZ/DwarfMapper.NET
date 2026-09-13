// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Model;
using DwarfMapper.Generator.Pipeline;

// Unit tests for MapperExtractor.PropagateContextToPublicMethodsSecondPass, widened from private to internal (owner
// ruling 2026-09-13: extract, expose, test — no deletion).
//
// Why no generator fixture reaches its patch arms: MarkRecursionCapableCallers runs immediately before it
// (MapperExtractor.cs) and already sets ConverterNeedsDepthCtx on every member / ctor argument whose converter is in
// recursionCapableNames. Under Preserve every __DwarfMap_Obj_* pair is force-marked recursion-capable before either
// pass, and the only names the first pass adds mid-loop are non-partial models that callers reach through an
// already-flagged edge. Probed with OVERLOADED names (the shape that hid 7635d71's regression): a declared Map(Root)
// whose Holder member or ctor argument is served by a [GenerateMap<Holder, HolderDto>] overload, two declared Map
// overloads, both pairs generated, and distinct names — all compiled, all members arrived already flagged, and lines
// 526-533 / 545-552 / 556-564 stayed at zero hits. The pass is kept as the ordering safety net its comment describes,
// and its contract is pinned here directly.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class PropagateContextSecondPassUnitTests
    {
        private const string Helper = "__DwarfMap_Obj_Holder";

        private static MapMethodModel Model(string name, MemberMap[] members, MemberMap[]? ctorArgs = null, bool isPartial = true) =>
            new(name, "public", "global::Demo.Dto", "global::Demo.Src", "s", true,
                EquatableArray.From(members),
                EquatableArray.From(Array.Empty<string>()),
                EquatableArray.From(Array.Empty<HookCall>()),
                false,
                "",
                EquatableArray.From(ctorArgs ?? Array.Empty<MemberMap>()),
                IsPartial: isPartial);

        private static HashSet<string> Names(params string[] names) => new(names, StringComparer.Ordinal);

        private static List<MapMethodModel> Run(MapMethodModel model, bool isPreserve = true, HashSet<string>? selfRecursive = null)
        {
            var methods = new List<MapMethodModel> { model };
            MapperExtractor.PropagateContextToPublicMethodsSecondPass(methods, Names(Helper), selfRecursive ?? Names(), 128, isPreserve);
            return methods;
        }

        [Fact]
        public void A_public_member_calling_a_recursion_capable_helper_without_ctx_is_patched()
        {
            var patched = Assert.Single(Run(Model("Map", [new MemberMap("H", "H", Helper), new MemberMap("A", "A")])));

            Assert.True(patched.IsRecursionCapable);
            Assert.Equal(128, patched.MaxDepth);
            Assert.True(patched.Members[0].ConverterNeedsDepthCtx);
            Assert.False(patched.Members[1].ConverterNeedsDepthCtx);
        }

        [Fact]
        public void A_public_ctor_argument_calling_a_recursion_capable_helper_without_ctx_is_patched()
        {
            var patched = Assert.Single(Run(Model("Map", [], [new MemberMap("h", "H", Helper)])));

            Assert.True(patched.IsRecursionCapable);
            Assert.True(patched.ConstructorArguments[0].ConverterNeedsDepthCtx);
        }

        [Fact]
        public void Outside_Preserve_mode_nothing_is_patched()
        {
            var model = Model("Map", [new MemberMap("H", "H", Helper)]);

            Assert.Same(model, Assert.Single(Run(model, isPreserve: false)));
        }

        [Fact]
        public void Synthesized_and_self_recursive_methods_are_left_to_the_first_pass()
        {
            var synthesized = Model(Helper, [new MemberMap("H", "H", Helper)], isPartial: false);
            var selfRecursive = Model("Map", [new MemberMap("H", "H", Helper)]);

            Assert.Same(synthesized, Assert.Single(Run(synthesized)));
            Assert.Same(selfRecursive, Assert.Single(Run(selfRecursive, selfRecursive: Names("Map"))));
        }

        [Fact]
        public void Edges_already_flagged_or_not_recursion_capable_leave_the_method_unmarked()
        {
            var model = Model("Map",
                [new MemberMap("H", "H", Helper, ConverterNeedsDepthCtx: true), new MemberMap("B", "B", "ToB")],
                [new MemberMap("a", "A"), new MemberMap("h", "H", Helper, ConverterNeedsDepthCtx: true), new MemberMap("c", "C", "ToC")]);

            Assert.Same(model, Assert.Single(Run(model)));
        }
    }
}
