// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>A compilation name used once leaves no cached baseline behind; a name used again gets one.</b>
    ///     <para>
    ///         Round 34 (E10). <c>GeneratorTestHarness.BuildCompilation</c> kept one empty compilation per assembly name
    ///         for the host's lifetime, so the reference binding is paid once per name. <c>EmitAssembly</c>,
    ///         <c>RunAndEmit</c> and several tests give every compilation a unique name, so each of those kept an entry
    ///         nothing would ever read: 1,635 in build-test's run, 5,319 in the deep tier. Measured, they were small
    ///         (the deep tier's live heap stayed at 420-440 MB either way); what this holds is that the cache keeps only
    ///         what it is for.
    ///     </para>
    /// </summary>
    public class BaselineCacheTests
    {
        [Fact]
        public void A_name_used_once_leaves_no_baseline_behind()
        {
            var names = Enumerable.Range(0, 50).Select(_ => "BaselineCacheTests_" + Guid.NewGuid().ToString("N")).ToList();
            foreach (var name in names)
            {
                _ = GeneratorTestHarness.BuildCompilation(name, "public class C { }");
            }

            Assert.DoesNotContain(names, name => GeneratorTestHarness.HasCachedBaseline(name));
        }

        [Fact]
        public void A_name_asked_for_again_gets_its_baseline_cached()
        {
            var name = "BaselineCacheTests_Repeated_" + Guid.NewGuid().ToString("N");

            _ = GeneratorTestHarness.BuildCompilation(name, "public class C { }");
            Assert.False(GeneratorTestHarness.HasCachedBaseline(name));

            _ = GeneratorTestHarness.BuildCompilation(name, "public class D { }");
            Assert.True(GeneratorTestHarness.HasCachedBaseline(name));
        }
    }
}
