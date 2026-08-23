// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     The harness's metadata-reference set must be a property of the RUNTIME, not of whatever the test host
    ///     happened to load first.
    ///     <para>
    ///         Before the trusted-platform-assemblies union, <see cref="GeneratorTestHarness" /> swept
    ///         <see cref="AppDomain.CurrentDomain" />, which lists only assemblies something has already touched. A
    ///         fixture naming a type from a type-forwarded or merely-untouched assembly then failed to compile, and
    ///         the failure surfaced as a generator defect (CS0246/CS1069 inside a test's own expectations) rather
    ///         than as the harness gap it was. It reproduced in an IDE while <c>dotnet test</c> stayed green, because
    ///         the two runners load different sets — which is exactly the shape that wastes a debugging session.
    ///         Three separate assemblies had been patched in one at a time before the class was closed.
    ///     </para>
    /// </summary>
    public sealed class HarnessReferenceSetTests
    {
        /// <summary>
        ///     Assemblies whose types appear in test fixture sources. This is a REQUIREMENT list, not an
        ///     allowlist: entries state what must be reachable, so adding one tightens the contract.
        /// </summary>
        private static string[] RequiredByFixtures => GeneratorTestHarness.RequiredFrameworkAssemblies;

        private static HashSet<string> ReferencedSimpleNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in GeneratorTestHarness.ReferenceSet)
                if (reference.Display is { Length: > 0 } display)
                {
                    names.Add(Path.GetFileNameWithoutExtension(display));
                }

            return names;
        }

        [Fact]
        public void Every_assembly_the_fixtures_name_is_reachable_from_the_harness()
        {
            var referenced = ReferencedSimpleNames();
            var missing = RequiredByFixtures.Where(r => !referenced.Contains(r)).ToArray();

            Assert.True(missing.Length == 0,
                "The harness cannot compile a fixture that names a type from: " +
                string.Join(", ", missing) +
                ". A fixture using one of these compiles under one runner and fails under another, and the failure " +
                "reads as a generator defect (CS0246 / CS1069 in the test's own source) rather than as this gap. " +
                "The reference set is built in GeneratorTestHarness.BuildReferences.");
        }

        /// <summary>
        ///     The anti-vacuity half. The union degrades SILENTLY: if the runtime stopped publishing
        ///     TRUSTED_PLATFORM_ASSEMBLIES, <c>BuildReferences</c> would quietly fall back to the load-order-
        ///     dependent sweep and the test above could still pass on a host that happens to have loaded
        ///     everything — a green proving nothing. Assert the data source itself is present and contributing.
        /// </summary>
        [Fact]
        public void The_trusted_platform_assembly_list_is_present_and_contributes()
        {
            var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            Assert.False(string.IsNullOrEmpty(tpa),
                "TRUSTED_PLATFORM_ASSEMBLIES is empty, so the harness's reference set has silently degraded to " +
                "the loaded-assembly sweep it was written to replace. Every fixture that names an untouched " +
                "assembly is now one test-ordering change away from a spurious CS0246.");

            var offered = new HashSet<string>(
                tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(n => !string.IsNullOrEmpty(n))!,
                StringComparer.OrdinalIgnoreCase);

            var unofferable = RequiredByFixtures.Where(r => !offered.Contains(r)).ToArray();

            Assert.True(unofferable.Length == 0,
                "TRUSTED_PLATFORM_ASSEMBLIES cannot supply: " +
                string.Join(", ", unofferable) +
                ". The fixture requirement above is then satisfied only if the host happens to have loaded them, " +
                "which is precisely the load-order dependence this replaced — the name is misspelled, or the " +
                "assembly is not part of this runtime.");

            // Deliberately NOT asserted: that TPA supplies a name the loaded sweep lacks. That was this test's
            // first form, and it red-flagged a healthy tree — running the whole assembly loads every required
            // assembly along the way, so TPA contributes nothing "new" and the check inverted into a false
            // failure. It was order-dependent, which is the defect it was written to guard against. What is
            // order-independent, and what actually matters, is that the fallback source COULD satisfy every
            // requirement whatever the host has touched.
        }
    }
}
