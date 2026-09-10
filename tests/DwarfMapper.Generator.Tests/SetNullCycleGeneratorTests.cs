// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Plan 19 Part C — OnCycle = SetNull (None-mode cycle breaking).
    ///     Generator-level assertions over the emitted on-stack guard and DWARF037.
    /// </summary>
    public class SetNullCycleGeneratorTests
    {
        private const string SelfRefNode = """
                                           using DwarfMapper;
                                           namespace Demo;
                                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                                           """;

        // ── 1. Compiles clean ───────────────────────────────────────────────────────
        [Fact]
        public void SetNull_attribute_compiles_without_error()
        {
            var src = SelfRefNode +
                      """
                      [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                      public partial class M { public partial NodeDto Map(Node n); }
                      """;
            GeneratorAssert.CompilesClean(src);
        }

        // ── 2. Emits the on-stack guard (TryEnterNode + ExitNode + try/finally) ──────
        [Fact]
        public void SetNull_emits_on_stack_guard_in_recursion_capable_method()
        {
            var src = SelfRefNode +
                      """
                      [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                      public partial class M { public partial NodeDto Map(Node n); }
                      """;
            var (_, generated) = GeneratorTestHarness.Run(src);
            Assert.Contains("TryEnterNode", generated, StringComparison.Ordinal);
            Assert.Contains("ExitNode", generated, StringComparison.Ordinal);
            Assert.Contains("finally", generated, StringComparison.Ordinal);
            // Back-edge breaks with null.
            Assert.Contains("return null!", generated, StringComparison.Ordinal);
        }

        // ── 3. Public entry allocates the context with setNull: true ────────────────
        [Fact]
        public void SetNull_public_entry_allocates_setNull_context()
        {
            var src = SelfRefNode +
                      """
                      [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                      public partial class M { public partial NodeDto Map(Node n); }
                      """;
            var (_, generated) = GeneratorTestHarness.Run(src);
            Assert.Contains("setNull: true", generated, StringComparison.Ordinal);
            // Must NOT allocate the Preserve identity map.
            Assert.DoesNotContain("SetReference", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("TryGetReference", generated, StringComparison.Ordinal);
        }

        // ── 4. None + Throw (default) does NOT emit the guard ────────────────────────
        [Fact]
        public void Default_OnCycle_Throw_does_not_emit_guard()
        {
            var src = SelfRefNode +
                      """
                      [DwarfMapper]
                      public partial class M { public partial NodeDto Map(Node n); }
                      """;
            var (_, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain("TryEnterNode", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("setNull: true", generated, StringComparison.Ordinal);
            // The depth guard is still present (None-mode backstop).
            Assert.Contains("DwarfMappingDepthException", generated, StringComparison.Ordinal);
        }

        // ── 5. Depth guard is preserved INSIDE the SetNull guarded body ─────────────
        [Fact]
        public void SetNull_keeps_depth_backstop_for_acyclic_chains()
        {
            var src = SelfRefNode +
                      """
                      [DwarfMapper(OnCycle = OnCycleStrategy.SetNull, MaxDepth = 8)]
                      public partial class M { public partial NodeDto Map(Node n); }
                      """;
            var (_, generated) = GeneratorTestHarness.Run(src);
            Assert.Contains("DwarfMappingDepthException", generated, StringComparison.Ordinal);
        }

        // ── 6. Non-recursive (acyclic) pair is unaffected — no guard, zero overhead ──
        [Fact]
        public void SetNull_acyclic_pair_has_no_guard()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Flat    { public int V { get; set; } public string S { get; set; } = ""; }
                               public class FlatDto { public int V { get; set; } public string S { get; set; } = ""; }
                               [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                               public partial class M { public partial FlatDto Map(Flat f); }
                               """;
            var (diags, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            Assert.DoesNotContain("TryEnterNode", generated, StringComparison.Ordinal);
        }

        // ── 7. DWARF037: OnCycle = SetNull together with Preserve → warning ─────────
        [Fact]
        public void SetNull_under_Preserve_reports_DWARF037_warning()
        {
            var src = SelfRefNode +
                      """
                      [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve, OnCycle = OnCycleStrategy.SetNull)]
                      public partial class M { public partial NodeDto Map(Node n); }
                      """;
            var (diags, generated) = GeneratorTestHarness.Run(src);
            var d037 = diags.SingleOrDefault(d => d.Id == "DWARF037");
            Assert.NotNull(d037);
            Assert.Equal(DiagnosticSeverity.Warning, d037.Severity);
            // No error — it's only a warning, and Preserve still reconstructs.
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            // Behaviour falls back to Preserve (identity map), NOT the on-stack guard.
            Assert.Contains("TryGetReference", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("TryEnterNode", generated, StringComparison.Ordinal);
        }

        // ── 8. None + Throw does NOT report DWARF037 ────────────────────────────────
        [Fact]
        public void None_mode_SetNull_does_not_report_DWARF037()
        {
            var src = SelfRefNode +
                      """
                      [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                      public partial class M { public partial NodeDto Map(Node n); }
                      """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diags, d => d.Id == "DWARF037");
        }

        // ── 9. SetNull threads ctx through a collection-element mapper (no fresh ctx) ─
        [Fact]
        public void SetNull_collection_element_threads_ctx_into_helper()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Tree    { public int V { get; set; } public List<Tree>? Children { get; set; } }
                               public class TreeDto { public int V { get; set; } public List<TreeDto>? Children { get; set; } }
                               [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                               public partial class M { public partial TreeDto Map(Tree t); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            // The collection helper must accept (ctx, depth) and forward them to the element mapper —
            // proving one shared context flows across the collection edge (no fresh-context re-entry).
            Assert.Contains("DwarfRefContext ctx, int depth", generated, StringComparison.Ordinal);
            Assert.Contains("ctx, depth + 1", generated, StringComparison.Ordinal);
            Assert.Contains("TryEnterNode", generated, StringComparison.Ordinal);
            // Must NOT register-before-fill (that is Preserve-only).
            Assert.DoesNotContain("SetReference", generated, StringComparison.Ordinal);
        }

        // ── 10. SetNull threads ctx through a dictionary-value mapper ────────────────
        [Fact]
        public void SetNull_dictionary_value_threads_ctx_into_helper()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Node    { public int V { get; set; } public Dictionary<string, Node>? E { get; set; } }
                               public class NodeDto { public int V { get; set; } public Dictionary<string, NodeDto>? E { get; set; } }
                               [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                               public partial class M { public partial NodeDto Map(Node n); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("DwarfRefContext ctx, int depth", generated, StringComparison.Ordinal);
            Assert.Contains("TryEnterNode", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("SetReference", generated, StringComparison.Ordinal);
        }

        // ── 11. SetNull + a ctor-only destination: back-edge threads through the CONSTRUCTOR ─
        // EmitSetNullGuardedBody's hasCtorArgs=true / hasInitMembers=false arm (round-30 coverage
        // sweep): every prior SetNull fixture used an object-initializer destination, so a
        // constructor-only target under OnCycle=SetNull had never been generated once. The
        // recursive back-edge is the SAME AppendValueExpression call used for a member — a
        // second null-through-a-ctor-arg call site the emitter had never actually taken.
        [Fact]
        public void SetNull_ctor_only_destination_self_cycle_compiles_clean()
        {
            // Source is a mutable class (a record source could never actually BE made cyclic — an
            // immutable positional type can't reference itself before it exists); the ctor-only
            // shape lives entirely on the destination.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class SnCtorOnly    { public int V { get; set; } public SnCtorOnly? Next { get; set; } }
                               public record SnCtorOnlyDto(int V, SnCtorOnlyDto? Next);
                               [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                               public partial class M { public partial SnCtorOnlyDto Map(SnCtorOnly n); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("TryEnterNode", generated, StringComparison.Ordinal);
            // Constructed inline (return new(...);), not via an object initializer.
            Assert.Contains("return new global::Demo.SnCtorOnlyDto(", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("SetReference", generated, StringComparison.Ordinal);
        }

        // ── 12. SetNull + ctor args PLUS a settable member: the sibling arm ──────────────
        // hasCtorArgs=true / hasInitMembers=true: the constructor call is followed by an object
        // initializer block. Here the ctor arg is scalar (never cyclic) and the recursive
        // back-edge is the settable member — the emitter's other unexercised combination.
        [Fact]
        public void SetNull_ctor_args_with_extra_member_self_cycle_compiles_clean()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class SnCtorPlus    { public int V { get; set; } public SnCtorPlus? Next { get; set; } }
                               public record SnCtorPlusDto(int V) { public SnCtorPlusDto? Next { get; set; } }
                               [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                               public partial class M { public partial SnCtorPlusDto Map(SnCtorPlus n); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("TryEnterNode", generated, StringComparison.Ordinal);
            Assert.Contains("new global::Demo.SnCtorPlusDto(", generated, StringComparison.Ordinal);
            // The trailing object-initializer assigns the recursive member.
            Assert.Contains("Next = ", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("SetReference", generated, StringComparison.Ordinal);
        }

        // ── 13. DWARF108: a struct destination behind a recursion-capable pair ───────────────
        // The probe that found the real defect: EmitSetNullGuardedBody's back-edge is an
        // unconditional `return null!;`, which is CS0037 against a non-nullable value-type return.
        // The extractor now detects this and falls back to the plain depth-guarded body instead of
        // emitting code that fails to compile.
        [Fact]
        public void SetNull_struct_destination_reports_DWARF108_and_falls_back_to_depth_guard()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Node    { public int V { get; set; } public List<Node>? Children { get; set; } }
                               public struct NodeDto { public int V { get; set; } public List<NodeDto>? Children { get; set; } }
                               [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                               public partial class M { public partial NodeDto Map(Node n); }
                               """;
            var (diags, generated) = GeneratorTestHarness.Run(src);

            // TWO independent MapMethodModel entries carry the value-type return here — the PUBLIC
            // entry `Map` (return type NodeDto directly) and the synthesized element helper the
            // List<Node> edge needs — and both would independently hit CS0037, so both are named.
            var d108s = diags.Where(d => d.Id == "DWARF108").ToList();
            Assert.Equal(2, d108s.Count);
            Assert.All(d108s, d =>
            {
                Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
                Assert.Contains("NodeDto", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                    StringComparison.Ordinal);
            });
            Assert.Contains(d108s, d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
                .Contains("'Map'", StringComparison.Ordinal));
            // No error: the fallback is a compiling, safe body — proven below.
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);

            // Falls back to the plain None+Throw depth-guarded body EVERYWHERE: no on-stack guard at
            // all survives, on either the public entry or the synthesized element helper.
            Assert.DoesNotContain("TryEnterNode", generated, StringComparison.Ordinal);
            Assert.Contains("DwarfMappingDepthException", generated, StringComparison.Ordinal);

            // And it actually compiles — this is what CS0037 would have broken before the fix.
            GeneratorAssert.EmitsCompilableCode(src);
        }

        // ── 14. A reference-type destination on the SAME shape does NOT report DWARF108 ──────
        [Fact]
        public void SetNull_class_destination_through_a_collection_edge_does_not_report_DWARF108()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Node    { public int V { get; set; } public List<Node>? Children { get; set; } }
                               public class NodeDto { public int V { get; set; } public List<NodeDto>? Children { get; set; } }
                               [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                               public partial class M { public partial NodeDto Map(Node n); }
                               """;
            var (diags, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diags, d => d.Id == "DWARF108");
            Assert.Contains("TryEnterNode", generated, StringComparison.Ordinal);
        }

        // ── 15. SetNull + [BeforeMap] on the recursion-capable public entry ──────────────────
        // EmitSetNullGuardedBody's before-hook loop (isPublicMethod branch) had zero executions:
        // every prior SetNull fixture declared no hooks at all.
        [Fact]
        public void SetNull_public_entry_runs_BeforeMap_hook_inside_the_guard()
        {
            var src = SelfRefNode +
                      """
                      [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                      public partial class M
                      {
                          public partial NodeDto Map(Node n);
                          [BeforeMap] private static void Check(Node n) { }
                      }
                      """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("TryEnterNode", generated, StringComparison.Ordinal);
            Assert.Contains("Check(n);", generated, StringComparison.Ordinal);
        }

        // ── 16. SetNull + a two-param [AfterMap] hook (TakesSource=true) ─────────────────────
        // EmitSetNullGuardedBody's after-hook loop reads TakesSource per hook; only the
        // one-parameter form had ever been exercised under SetNull.
        [Fact]
        public void SetNull_after_hook_with_source_param_is_called_with_both_arguments()
        {
            var src = SelfRefNode +
                      """
                      [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                      public partial class M
                      {
                          public partial NodeDto Map(Node n);
                          [AfterMap] private static void Finish(Node n, NodeDto d) { }
                      }
                      """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("TryEnterNode", generated, StringComparison.Ordinal);
            Assert.Contains("Finish(n, __dwarf_target);", generated, StringComparison.Ordinal);
        }
    }
}
