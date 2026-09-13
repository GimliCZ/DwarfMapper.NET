// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Model;
using DwarfMapper.Generator.Pipeline;

// Unit tests for MapperExtractor.SynthesizePreserveDispatchWrappers, widened from private to internal (owner ruling
// 2026-09-13: extract, expose, test — no deletion).
//
// Why no generator fixture reaches its "dispatch method is not recursion-capable" skip: under Preserve every
// [MapDerivedType] arm that maps an object resolves to a __DwarfMap_Obj_* helper, which is force-marked recursion-capable,
// and ThreadContextThroughDispatchArms then marks the dispatch recursion-capable before this pass runs. Probed with a
// declared arm overload beside the dispatch (W2: the arm still used the helper), an identity arm
// `[MapDerivedType<Dog, Dog>]` (P2: refused as DWARF035 before this pass) and an implicit-operator arm (P3: still an
// object helper). The skip stays as the guard its comment names ("arm converters don't need ctx"), pinned here.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class SynthesizePreserveDispatchWrappersUnitTests
    {
        private const string AnimalFqn = "global::Demo.Animal";
        private const string AnimalDtoFqn = "global::Demo.AnimalDto";

        private static readonly string Wrapper = NestedMappingRegistry.BuildDispatchWrapperName(AnimalFqn, AnimalDtoFqn);

        private static MapMethodModel Dispatch(bool isRecursionCapable = true, bool isPartial = true) =>
            new("Map", "public", AnimalDtoFqn, AnimalFqn, "a", true,
                EquatableArray.From(Array.Empty<MemberMap>()),
                EquatableArray.From(Array.Empty<string>()),
                EquatableArray.From(Array.Empty<HookCall>()),
                false,
                "",
                IsPartial: isPartial,
                IsRecursionCapable: isRecursionCapable,
                DerivedTypeArms: EquatableArray.From(new[] { new DerivedTypeArm("global::Demo.Dog", "__DwarfMap_Obj_Dog", ConverterNeedsDepthCtx: true) }));

        private static MapMethodModel Caller(string name, MemberMap[] members, MemberMap[]? ctorArgs = null, bool isPartial = true, bool isRecursionCapable = false, int maxDepth = 64) =>
            new(name, "public", "global::Demo.ZooDto", "global::Demo.Zoo", "z", true,
                EquatableArray.From(members),
                EquatableArray.From(Array.Empty<string>()),
                EquatableArray.From(Array.Empty<HookCall>()),
                false,
                "",
                EquatableArray.From(ctorArgs ?? Array.Empty<MemberMap>()),
                IsPartial: isPartial,
                IsRecursionCapable: isRecursionCapable,
                MaxDepth: maxDepth);

        private static (List<MapMethodModel> Methods, HashSet<string> RecursionCapable, Dictionary<string, SynthesizedMethod> Synthesized) Run(
            bool isPreserve, Dictionary<string, SynthesizedMethod>? synthesized = null, params MapMethodModel[] models)
        {
            var methods = models.ToList();
            var recursionCapable = new HashSet<string>(StringComparer.Ordinal);
            synthesized ??= new Dictionary<string, SynthesizedMethod>(StringComparer.Ordinal);
            MapperExtractor.SynthesizePreserveDispatchWrappers(methods, recursionCapable, synthesized, 128, isPreserve);
            return (methods, recursionCapable, synthesized);
        }

        [Fact]
        public void A_dispatch_method_that_is_not_recursion_capable_gets_no_wrapper_and_its_callers_are_untouched()
        {
            var caller = Caller("ToZoo", [new MemberMap("First", "First", "Map")]);

            var (methods, recursionCapable, synthesized) = Run(isPreserve: true, null, Dispatch(isRecursionCapable: false), caller);

            Assert.Equal(caller, methods[1]);
            Assert.Empty(recursionCapable);
            Assert.Empty(synthesized);
        }

        [Fact]
        public void A_synthesized_dispatch_method_and_a_mapper_without_Preserve_get_no_wrapper()
        {
            var caller = Caller("ToZoo", [new MemberMap("First", "First", "Map")]);

            Assert.Equal(caller, Run(isPreserve: true, null, Dispatch(isPartial: false), caller).Methods[1]);
            Assert.Equal(caller, Run(isPreserve: false, null, Dispatch(), caller).Methods[1]);
        }

        [Fact]
        public void Member_and_constructor_argument_callers_are_redirected_and_a_declared_caller_is_promoted()
        {
            var caller = Caller("ToZoo",
                [new MemberMap("First", "First", "Map"), new MemberMap("Title", "Title"), new MemberMap("Other", "Other", "ToUpper")],
                [new MemberMap("second", "Second", "Map"), new MemberMap("name", "Name")]);

            var (methods, recursionCapable, synthesized) = Run(isPreserve: true, null, Dispatch(), caller);

            var patched = methods[1];
            Assert.Equal(new MemberMap("First", "First", Wrapper, ConverterNeedsDepthCtx: true), patched.Members[0]);
            Assert.Equal(new MemberMap("Title", "Title"), patched.Members[1]);
            Assert.Equal(new MemberMap("Other", "Other", "ToUpper"), patched.Members[2]);
            Assert.Equal(new MemberMap("second", "Second", Wrapper, ConverterNeedsDepthCtx: true), patched.ConstructorArguments[0]);
            Assert.Equal(new MemberMap("name", "Name"), patched.ConstructorArguments[1]);
            Assert.True(patched.IsRecursionCapable);
            Assert.True(patched.IsPreserveMode);
            Assert.Equal(128, patched.MaxDepth);
            Assert.Contains(Wrapper, recursionCapable);
            Assert.Equal(Wrapper, Assert.Single(synthesized).Key);
        }

        [Fact]
        public void A_synthesized_caller_keeps_its_own_recursion_flags()
        {
            var caller = Caller("__DwarfMap_Obj_Zoo", [new MemberMap("First", "First", "Map")], isPartial: false, maxDepth: 64);

            var patched = Run(isPreserve: true, null, Dispatch(), caller).Methods[1];

            Assert.Equal(Wrapper, patched.Members[0].ConverterMethod);
            Assert.False(patched.IsRecursionCapable);
            Assert.False(patched.IsPreserveMode);
            Assert.Equal(64, patched.MaxDepth);
        }

        [Fact]
        public void An_already_flagged_caller_is_not_redirected_so_the_unused_wrapper_is_never_synthesized()
        {
            var caller = Caller("ToZoo",
                [new MemberMap("First", "First", "Map", ConverterNeedsDepthCtx: true)],
                [new MemberMap("second", "Second", "Map", ConverterNeedsDepthCtx: true)],
                isRecursionCapable: true);

            var (methods, recursionCapable, synthesized) = Run(isPreserve: true, null, Dispatch(), caller);

            Assert.Equal(caller, methods[1]);
            Assert.Empty(recursionCapable);
            Assert.Empty(synthesized);
        }

        [Fact]
        public void A_wrapper_already_in_the_synthesized_table_is_kept_and_still_marked_recursion_capable()
        {
            var existing = new SynthesizedMethod(Wrapper, "// already emitted");
            var table = new Dictionary<string, SynthesizedMethod>(StringComparer.Ordinal) { [Wrapper] = existing };

            var (_, recursionCapable, synthesized) = Run(isPreserve: true, table, Dispatch(), Caller("ToZoo", [new MemberMap("First", "First", "Map")]));

            Assert.Same(existing, synthesized[Wrapper]);
            Assert.Contains(Wrapper, recursionCapable);
        }
    }
}
