// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Round-30 item H. Every per-call member of the shipped runtime must carry a declared COST CONTRACT —
    ///     a statement of what its cost is allowed to do as state grows and as calls accumulate — backed by a
    ///     named test, or an exemption with a reason.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The gap this closes.</b> The repository already gates cost hard, with exact-byte allocation
    ///         pins in <c>allocation-baseline.json</c> that fail on one byte of drift in either direction.
    ///         Round 30 still shipped a defect costing 56 KB per ambient call, because the pins measure
    ///         ABSOLUTE bytes at ONE scale and the defect was a SLOPE: <c>80 + 24·N</c> bytes against the
    ///         number of registered maps. At benchmark scale (N ≈ 10) it was 296 B and looked correct; at
    ///         consumer scale it was 56 KB. A pin at a single point on a line cannot see the line's gradient.
    ///     </para>
    ///     <para>
    ///         This is the third time this repository has been bitten by measuring one point of a curve.
    ///         <c>BenchmarkCoverageSelfValidationTests</c> records ISSUE-019 — an unknown-count source into an
    ///         array target allocated two buffers on every map for as long as the project existed, and no
    ///         benchmark covered the shape. Round 29's collection sweep closed with the same sentence in
    ///         different words: every collection claim rested on one element count. The pattern is not
    ///         "someone forgot a test", it is "the gate varied the wrong dimension", and only a scan can hold
    ///         a dimension open.
    ///     </para>
    ///     <para>
    ///         <b>Why a scan and not discipline.</b> Adding a public per-call member is the moment the
    ///         contract is cheapest to state and easiest to forget. The build fails until someone classifies
    ///         it, which is the same mechanism <see cref="RuntimeCoverageScanTests" /> uses for behavioural
    ///         coverage and <c>BenchmarkCoverageSelfValidationTests</c> uses for shape coverage.
    ///     </para>
    /// </remarks>
    public class CostContractScanTests
    {
        /// <summary>
        ///     The types whose members are called PER MAPPING OPERATION. Cost is a contract on these and only
        ///     these: an attribute is read by the generator at compile time and never executes, and an
        ///     exception is constructed on a failure path where allocation is not the concern.
        /// </summary>
        private static readonly string[] PerCallTypes =
        [
            "DwarfMapper.DwarfMapperRegistry",
            "DwarfMapper.DwarfRefContext",
            "DwarfMapper.IDwarfMapper",
            "DwarfMapper.DwarfMapperFacade"
        ];

        /// <summary>
        ///     member signature → the test that contracts it, or an exemption beginning with "EXEMPT:" and a
        ///     reason. A bare entry is not allowed; the value is what someone reads when they wonder whether a
        ///     member is fast.
        /// </summary>
        private static readonly Dictionary<string, string> Contracts = new(StringComparer.Ordinal)
        {
            // ── DwarfMapperRegistry: the ambient dispatch surface ─────────────────────────────────────────
            ["DwarfMapperRegistry.Map"] =
                "AmbientDispatchAllocationRuntimeTests.Interface_path_dispatch_does_not_allocate_a_copy_of_the_registry"
                + " + AmbientCostContractTests.Interface_dispatch_cost_is_flat_over_time"
                + " + AmbientCostContractTests.Exact_type_dispatch_cost_is_flat_over_time",
            ["DwarfMapperRegistry.Update"] = "AmbientCostContractTests.Update_dispatch_cost_is_flat_over_time",
            ["DwarfMapperRegistry.IsProvided"] = "AmbientCostContractTests.Registry_predicates_allocate_nothing_and_stay_flat",
            ["DwarfMapperRegistry.IsAmbiguous"] = "AmbientCostContractTests.Registry_predicates_allocate_nothing_and_stay_flat",
            ["DwarfMapperRegistry.IsUpdateProvided"] = "AmbientCostContractTests.Registry_predicates_allocate_nothing_and_stay_flat",
            ["DwarfMapperRegistry.IsUpdateAmbiguous"] = "AmbientCostContractTests.Registry_predicates_allocate_nothing_and_stay_flat",
            ["DwarfMapperRegistry.TryGet"] = "AmbientCostContractTests.Registry_predicates_allocate_nothing_and_stay_flat",
            ["DwarfMapperRegistry.Provided"] = "AmbientCostContractTests.Provided_is_linear_by_design_but_must_not_grow_over_time",

            // Registration runs once per assembly load from a generated module initializer. Its cost is
            // deliberately O(registry) — the copy-on-write swap that makes LOOKUP free — so a flatness
            // contract would forbid the very design that fixed the defect. What matters is that it is not on
            // a per-call path, and RegistryAppendOnlyTests already pins that it is append-only.
            ["DwarfMapperRegistry.Register"] =
                "EXEMPT: load-time only, and O(registry) BY DESIGN — the copy-on-write swap is what buys the "
                + "flat lookup contracted above. Called once per pair per assembly load.",
            ["DwarfMapperRegistry.RegisterUpdate"] =
                "EXEMPT: load-time only, as Register.",

            // ── DwarfRefContext: allocated per invocation, threaded through recursion ─────────────────────
            ["DwarfRefContext.TryGetReference"] = "AmbientCostContractTests.Ref_context_cost_is_flat_over_time",
            ["DwarfRefContext.SetReference"] = "AmbientCostContractTests.Ref_context_cost_is_flat_over_time",
            ["DwarfRefContext.TryEnterNode"] = "AmbientCostContractTests.Ref_context_cost_is_flat_over_time",
            ["DwarfRefContext.ExitNode"] = "AmbientCostContractTests.Ref_context_cost_is_flat_over_time",
            ["DwarfRefContext.MaxDepth"] =
                "EXEMPT: returns a readonly int field set in the constructor; there is no state for a cost to "
                + "depend on.",

            // ── The ambient facade: thin generic wrappers over the registry ───────────────────────────────
            ["IDwarfMapper.Map"] =
                "EXEMPT: interface declaration. The implementation is DwarfMapperFacade.Map, contracted below.",
            ["DwarfMapperFacade.Map"] =
                "EXEMPT: forwards directly to DwarfMapperRegistry.Map/Update with no state of its own, so its "
                + "cost is theirs plus a generic call. Contracted at the registry."
        };

        /// <summary>
        ///     Every public per-call member is classified. A new one fails the build until someone says what
        ///     its cost may do.
        /// </summary>
        [Fact]
        public void Every_per_call_runtime_member_has_a_declared_cost_contract()
        {
            var members = PerCallMembers();

            var unclassified = members
                .Where(m => !Contracts.ContainsKey(m))
                .OrderBy(m => m, StringComparer.Ordinal)
                .ToList();

            Assert.True(unclassified.Count == 0,
                "these public per-call runtime members have no declared cost contract:" +
                Environment.NewLine + string.Join(Environment.NewLine, unclassified.Select(m => "  " + m)) +
                Environment.NewLine + Environment.NewLine +
                "Add each to CostContractScanTests.Contracts, naming either the test that pins its cost " +
                "SLOPE or an 'EXEMPT: <reason>'. Cost is a contract on this surface because a consumer pays " +
                "it on every mapping operation, and absolute-byte benchmark pins cannot see a slope.");
        }

        /// <summary>
        ///     No contract may name a test that does not exist. Without this the table rots into a list of
        ///     reassuring strings — the exact failure mode this repository calls a vacuous green.
        /// </summary>
        [Fact]
        public void Every_named_cost_test_exists_in_the_integration_suite()
        {
            var suite = Directory
                .EnumerateFiles(Path.Combine(RepoPaths.Tests, "DwarfMapper.IntegrationTests"), "*.cs",
                    SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                StringComparison.Ordinal)
                            && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                StringComparison.Ordinal))
                .Select(File.ReadAllText)
                .ToList();

            Assert.NotEmpty(suite);

            var missing = new List<string>();
            foreach (var (member, contract) in Contracts)
            {
                if (contract.StartsWith("EXEMPT:", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var testName in contract.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    var method = testName[(testName.LastIndexOf('.') + 1)..];
                    if (!suite.Any(s => s.Contains(method, StringComparison.Ordinal)))
                    {
                        missing.Add($"{member} -> {testName}");
                    }
                }
            }

            Assert.True(missing.Count == 0,
                "these cost contracts name tests that do not exist in DwarfMapper.IntegrationTests:" +
                Environment.NewLine + string.Join(Environment.NewLine, missing.Select(m => "  " + m)));
        }

        /// <summary>
        ///     Non-vacuity, in the shape <see cref="SelfAuditNonVacuityTests" /> established: the two scans
        ///     above are subset checks, and a subset check over an empty universe passes while verifying
        ///     nothing. If reflection stops finding the runtime surface — a moved type, a renamed assembly —
        ///     this is what goes red instead of everything silently going green.
        /// </summary>
        [Fact]
        public void The_per_call_surface_enumeration_is_not_empty()
        {
            var members = PerCallMembers();

            Assert.True(members.Count >= 12,
                $"only {members.Count} per-call runtime members were found. The registry alone declares " +
                "nine. The enumeration is broken, and both scans above are passing over an empty set.");

            Assert.Contains("DwarfMapperRegistry.Map", members);
            Assert.Contains("DwarfRefContext.TryGetReference", members);
        }

        /// <summary>
        ///     Public instance/static methods and properties on the per-call types, reduced to
        ///     <c>Type.Member</c> so overloads share one contract — an overload set is one cost story.
        /// </summary>
        private static HashSet<string> PerCallMembers()
        {
            var assembly = typeof(DwarfMapperAttribute).Assembly;
            var found = new HashSet<string>(StringComparer.Ordinal);

            foreach (var typeName in PerCallTypes)
            {
                var type = assembly.GetType(typeName);
                if (type is null)
                {
                    continue;
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static |
                                                       BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    // Property accessors are recorded under the property's own name below.
                    if (method.IsSpecialName)
                    {
                        continue;
                    }

                    found.Add($"{type.Name}.{method.Name}");
                }

                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static |
                                                            BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    found.Add($"{type.Name}.{property.Name}");
            }

            return found;
        }
    }
}
