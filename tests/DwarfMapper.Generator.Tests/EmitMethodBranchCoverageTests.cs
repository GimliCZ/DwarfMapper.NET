// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Round 30 coverage sweep — <c>MapEmitter.EmitMethod</c> is the single biggest CRAP hotspot in
    ///     the emitter (CrapScore 184 at the start of this file, 100% line / 90% branch measured on a
    ///     class-filtered <c>reportgenerator</c> pass). These close specific branches that a normal test
    ///     run never exercises: the depth-context arm of the top-level collection/dictionary shortcut, the
    ///     zero-member projection fallback, and the <c>[MapConstructor]</c> factory path's per-member
    ///     When/SkipIfSourceNull gates plus its own (separate from the ordinary construction path's)
    ///     after-hook loop.
    /// </summary>
    public class EmitMethodBranchCoverageTests
    {
        [Fact]
        public void TopLevelCollection_of_a_recursion_capable_element_pair_threads_the_public_ctx_and_depth_zero()
        {
            // Node/NodeDto is self-referential through Children, so the per-element converter is
            // recursion-capable (ConverterNeedsDepthCtx) and the public top-level method itself becomes
            // IsRecursionCapable — the only combination that reaches EmitMethod's
            // `if (tlm.ConverterNeedsDepthCtx) sb.Append(", ").Append(ctxVarName).Append(", 0");` branch.
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public class Node { public int V { get; set; } public List<Node>? Children { get; set; } }
                               public class NodeDto { public int V { get; set; } public List<NodeDto>? Children { get; set; } }
                               [DwarfMapper]
                               public partial class M { public partial List<NodeDto> Map(List<Node> items); }
                               """;
            var (diag, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("__dwarf_ctx, 0);", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void Projection_destination_with_no_public_settable_members_emits_an_empty_object_initializer()
        {
            // ProjectionMembers.Count == 0 with no Error means the destination genuinely has nothing to
            // assign (no public settable member and no explicit map). This used to be a THIRD emitter arm
            // (a "legacy flat Members path" looping over method.Members, always empty for a projection
            // method and so behaviourally dead) — removed; the member-init projection arm now handles
            // Count == 0 by producing an empty initializer body directly.
            const string src = """
                               using DwarfMapper; using System.Linq;
                               namespace Demo;
                               public class Src { public int Age { get; set; } }
                               public class Dst { }
                               [DwarfMapper] public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
                               """;
            var (diag, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("new global::Demo.Dst", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void MapConstructor_factory_path_gates_members_with_When_and_SkipIfSourceNull_and_runs_its_own_after_hooks()
        {
            // The [MapConstructor] factory path (EmitMethod.method.EmitFactoryMethod is not null) assigns
            // every settable member as its own statement — gated by When, SkipIfSourceNull, or neither —
            // and then runs an after-hook loop that is textually identical to, but a SEPARATE emission site
            // from, the ordinary construction path's loop further down EmitMethod. Two [AfterMap] hooks
            // (one plain, one taking the source by value and the target by ref) cover both true/false
            // states of TakesSource and TargetByRef in that loop.
            //
            // A declared partial method's OWN pair never reads [MapConstructor<S,T>] — only a
            // [GenerateMap<S,T>]-declared pair does (MapperExtractor.Phases.cs's genFactory wiring, gated on
            // decls.GenPairs.Exists(...)); a bare `public partial Dst Map(Src s)` here would silently fall
            // through to ordinary constructor-argument selection instead. So this pair is [GenerateMap]'d
            // with no declared method at all, matching the one shipped sample (44_FactoryConstruction.cs).
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src
                               {
                                   public string Name { get; set; } = "";
                                   public int Score { get; set; }
                                   public string? Note { get; set; }
                               }
                               public class Dst
                               {
                                   public Dst(string name) { Name = name; }
                                   public string Name { get; }
                                   public int Score { get; set; }
                                   public string Note { get; set; } = "";
                               }
                               [DwarfMapper(SkipNullSourceMembers = true)]
                               [GenerateMap<Src, Dst>]
                               [MapConstructor<Src, Dst>(nameof(Create))]
                               [MapProperty<Src, Dst>(nameof(Src.Score), nameof(Dst.Score), When = nameof(IsActive))]
                               public partial class M
                               {
                                   private static bool IsActive(Src s) => true;
                                   private static Dst Create(Src s) => new Dst(s.Name);
                                   [AfterMap] private static void Plain(Dst d) { }
                                   [AfterMap] private static void SourceAndRef(Src s, ref Dst d) { }
                               }
                               """;
            var (diag, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("var __dwarf_target = Create(src);", generated, StringComparison.Ordinal);
            Assert.Contains("if (IsActive(src)) __dwarf_target.Score = ", generated, StringComparison.Ordinal);
            Assert.Contains("if (src.Note is not null) __dwarf_target.Note = ", generated, StringComparison.Ordinal);
            Assert.Contains("Plain(__dwarf_target);", generated, StringComparison.Ordinal);
            Assert.Contains("SourceAndRef(src, ref __dwarf_target);", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void CtorArgs_with_init_members_defers_When_and_SkipIfSourceNull_members_out_of_the_initializer_block()
        {
            // hasCtorArgs && hasInitMembers: the ordinary construction path's object-initializer block
            // (`new T(name: s.Name) { ... }`) must skip any member that is UnflattenIntermediateFqn,
            // When-guarded, or SkipIfSourceNull — each is assigned afterward, by EmitDeferredAssignments,
            // not inline — while a plain settable member with none of those still gets its initializer
            // entry, so the empty-initializer shape from the abandoned factory-path fixture (all members
            // deferred) is NOT this branch's only reachable state.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src
                               {
                                   public string Name { get; set; } = "";
                                   public int Score { get; set; }
                                   public string? Note { get; set; }
                                   public int Count { get; set; }
                               }
                               public class Dst
                               {
                                   public Dst(string name) { Name = name; }
                                   public string Name { get; }
                                   public int Score { get; set; }
                                   public string Note { get; set; } = "";
                                   public int Count { get; set; }
                               }
                               [DwarfMapper(SkipNullSourceMembers = true)]
                               public partial class M
                               {
                                   [MapProperty(nameof(Src.Score), nameof(Dst.Score), When = nameof(IsActive))]
                                   public partial Dst Map(Src s);
                                   private static bool IsActive(Src s) => true;
                               }
                               """;
            var (diag, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
            // SkipNullSourceMembers only converts REFERENCE-type (or nullable-value) source members into a
            // SkipIfSourceNull deferral (MapperExtractor.Members.Phases.cs) — Count (int, a non-nullable
            // value type) is neither, so it stays a plain initializer entry alongside the ctor arg.
            Assert.Contains("new global::Demo.Dst(", generated, StringComparison.Ordinal);
            Assert.Contains("name: s.Name)", generated, StringComparison.Ordinal);
            Assert.Contains("Count = s.Count,", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("Score = s.Score,", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("Note = s.Note,", generated, StringComparison.Ordinal);
            Assert.Contains("if (IsActive(s)) __dwarf_target.Score = s.Score;", generated, StringComparison.Ordinal);
            Assert.Contains("if (s.Note is not null) __dwarf_target.Note = s.Note;", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void MapDerivedType_arm_that_is_self_referential_threads_the_dispatch_methods_own_ctx_and_depth_zero()
        {
            // Dog/DogDto is self-referential through Pup, so the synthesized arm converter is
            // recursion-capable (ConverterNeedsDepthCtx) — EmitDerivedDispatchBody's arm-emission branch.
            // Unlike EmitMethod's general preamble, a dispatch method is NEVER itself a synthesized
            // recursion-capable private helper (see the comment on EmitDerivedDispatchBody's ctxVarName),
            // so the call is always `(__s, __dwarf_ctx, 0)` — never `(__s, ctx, depth + 1)`.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; public Dog? Pup { get; set; } }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; public DogDto? Pup { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   public partial AnimalDto Map(Animal a);
                               }
                               """;
            var (diag, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("var __dwarf_ctx = new global::DwarfMapper.DwarfRefContext(64);", generated, StringComparison.Ordinal);
            Assert.Contains("__s, __dwarf_ctx, 0)", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src);
        }
    }
}
