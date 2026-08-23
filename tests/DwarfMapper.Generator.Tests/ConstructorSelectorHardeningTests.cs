// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Hardening coverage for ConstructorSelector — the audit found the happy paths covered but the
    ///     AllowNonPublic accessibility matrix, obsolete-ctor exclusion, the annotated-override usability filter
    ///     (a latent CS1620), and the most-params auto-pick all untested.
    /// </summary>
    public class ConstructorSelectorHardeningTests
    {
        // ── ISSUE-016: selection ranked by arity alone, ignoring whether the parameters can be satisfied ──

        /// <summary>
        ///     A target with two usable constructors where only the NARROWER one can be satisfied from the source.
        ///     Selecting purely by arity picks the wider ctor and reports DWARF024 for a parameter the user never
        ///     asked to bind, even though a fully mappable constructor was sitting right there.
        /// </summary>
        private const string TwoCtorsOnlyNarrowMappable = """
                                                          using DwarfMapper;
                                                          namespace Demo;
                                                          public class Extra { public string Name { get; set; } = ""; }
                                                          public class Src { public int Id { get; set; } }
                                                          public class Dst
                                                          {
                                                              public Dst(int id) { Id = id; }
                                                              public Dst(int id, Extra extra) { Id = id; Extra = extra; }
                                                              public int Id { get; }
                                                              public Extra? Extra { get; }
                                                          }
                                                          [DwarfMapper]
                                                          [GenerateMap<Src, Dst>]
                                                          public partial class M { }
                                                          """;

        // ── The latent CS1620 bug: an annotated ctor that is NOT usable (ref/out) must not be selected. ──
        [Fact]
        public void Annotated_ref_param_ctor_with_parameterless_falls_back_clean()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int X { get; set; } }
                             public class Dst
                             {
                                 public Dst() { }
                                 [DwarfMapperConstructor]
                                 public Dst(ref int x) { X = x; }
                                 public int X { get; set; }
                             }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M { }
                             """;

            var errors = GeneratorTestHarness.RunAndGetCompilationErrors(s);
            Assert.DoesNotContain(errors, d => d.Id == "CS1620");
            Assert.Empty(errors); // falls back to the parameterless object-initializer path
        }

        // ── AllowNonPublic accessibility matrix ──
        [Fact]
        public void Internal_ctor_without_flag_reports_DWARF026()
        {
            var (diags, _) = GeneratorTestHarness.Run(Internal("", false));
            Assert.Contains(diags, d => d.Id == "DWARF026");
        }

        [Fact]
        public void Internal_ctor_with_flag_compiles_clean()
        {
            var errors = GeneratorTestHarness.RunAndGetCompilationErrors(Internal("internal", true));
            Assert.Empty(errors);
        }

        [Fact]
        public void Protected_internal_ctor_with_flag_compiles_clean()
        {
            var errors = GeneratorTestHarness.RunAndGetCompilationErrors(Internal("protected internal", true));
            Assert.Empty(errors);
        }

        [Fact]
        public void Private_ctor_with_flag_still_reports_DWARF026()
        {
            // private is unreachable from the assembly scope even with the flag.
            var (diags, _) = GeneratorTestHarness.Run(OnlyCtor("private", true));
            Assert.Contains(diags, d => d.Id == "DWARF026");
        }

        [Fact]
        public void Protected_ctor_with_flag_still_reports_DWARF026()
        {
            var (diags, _) = GeneratorTestHarness.Run(OnlyCtor("protected", true));
            Assert.Contains(diags, d => d.Id == "DWARF026");
        }

        // ── Obsolete ctor exclusion ──
        [Fact]
        public void Obsolete_ctor_is_skipped_in_favour_of_a_usable_one()
        {
            const string s = """
                             using System;
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int X { get; set; } }
                             public class Dst
                             {
                                 [Obsolete] public Dst(int x, string note) { X = x; }
                                 public Dst(int x) { X = x; }
                                 public int X { get; }
                             }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M { }
                             """;

            var errors = GeneratorTestHarness.RunAndGetCompilationErrors(s);
            Assert.Empty(errors);
        }

        [Fact]
        public void Only_obsolete_ctor_reports_DWARF026()
        {
            const string s = """
                             using System;
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int X { get; set; } }
                             public class Dst { [Obsolete] public Dst(int x) { X = x; } public int X { get; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M { }
                             """;

            var (diags, _) = GeneratorTestHarness.Run(s);
            Assert.Contains(diags, d => d.Id == "DWARF026");
        }

        // ── Two annotated ctors, NO parameterless → DWARF025 (the no-parameterless ambiguity branch) ──
        [Fact]
        public void Two_annotated_ctors_without_parameterless_report_DWARF025()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int X { get; set; } public string Y { get; set; } = ""; }
                             public class Dst
                             {
                                 [DwarfMapperConstructor] public Dst(int x) { X = x; }
                                 [DwarfMapperConstructor] public Dst(int x, string y) { X = x; Y = y; }
                                 public int X { get; } public string Y { get; } = "";
                             }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M { }
                             """;

            var (diags, _) = GeneratorTestHarness.Run(s);
            Assert.Contains(diags, d => d.Id == "DWARF025");
        }

        // ── Most-params auto-selection among usable ctors (Policy 5, non-tie) ──
        [Fact]
        public void Most_params_ctor_is_auto_selected()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int X { get; set; } public string Y { get; set; } = ""; }
                             public class Dst
                             {
                                 public Dst(int x) { X = x; }
                                 public Dst(int x, string y) { X = x; Y = y; }
                                 public int X { get; } public string Y { get; } = "";
                             }
                             [DwarfMapper(CaseInsensitive = true)]
                             [GenerateMap<Src, Dst>]
                             public partial class M { }
                             """;

            var source = GeneratorTestHarness.Run(s).GeneratedSource;
            Assert.Contains("x:", source, StringComparison.Ordinal);
            Assert.Contains("y:", source, StringComparison.Ordinal); // the 2-arg ctor was chosen, not the 1-arg one
        }

        [Fact]
        public void Prefers_a_mappable_ctor_over_a_wider_unmappable_one()
        {
            var (diags, _) = GeneratorTestHarness.Run(TwoCtorsOnlyNarrowMappable);
            Assert.DoesNotContain(diags, d => d.Id == "DWARF024");
        }

        /// <summary>
        ///     Asserting "it compiles" would prove nothing here: on the arity-only selector the method is skipped
        ///     after DWARF024, so the output compiles either way. The discriminating fact is that a Dst is actually
        ///     constructed — via the one-argument form.
        /// </summary>
        [Fact]
        public void Prefers_a_mappable_ctor_and_actually_emits_the_construction()
        {
            var (_, gen) = GeneratorTestHarness.Run(TwoCtorsOnlyNarrowMappable);
            Assert.Contains("new global::Demo.Dst(", gen, StringComparison.Ordinal);
            Assert.DoesNotContain("extra:", gen, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The genuinely-unmappable case must stay loud: when NO constructor can be satisfied, DWARF024 is still
        ///     reported rather than silently picking one and emitting broken code.
        /// </summary>
        [Fact]
        public void No_mappable_ctor_still_reports_DWARF024()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Extra { public string Name { get; set; } = ""; }
                             public class Src { public int Id { get; set; } }
                             public class Dst
                             {
                                 public Dst(Extra extra) { Extra = extra; }
                                 public Dst(Extra extra, Extra other) { Extra = extra; }
                                 public Extra? Extra { get; }
                             }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M { }
                             """;

            var (diags, _) = GeneratorTestHarness.Run(s);
            Assert.Contains(diags, d => d.Id == "DWARF024");
        }

        /// <summary>
        ///     ISSUE-016 audit regression: a ctor parameter bound ONLY via a pair-scoped [MapProperty&lt;S,T&gt;]
        ///     rename must count as satisfiable. The satisfiability check runs during constructor selection, which
        ///     used to see only method-level renames — so the wide ctor's 'code' param (bound by the class-level
        ///     rename, not a same-named source member) looked unsatisfiable, the narrow ctor was chosen, and the
        ///     get-only Code member then surfaced DWARF008. The fix merges reverse/pair renames before selection.
        /// </summary>
        [Fact]
        public void Pair_scoped_rename_into_ctor_param_keeps_the_wide_ctor()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } public int LegacyCode { get; set; } }
                             public class Dst
                             {
                                 public Dst(int id) { Id = id; }
                                 public Dst(int id, int code) { Id = id; Code = code; }
                                 public int Id { get; }
                                 public int Code { get; }
                             }
                             [DwarfMapper]
                             [MapProperty<Src, Dst>("LegacyCode", "code")]
                             public partial class M { public partial Dst Map(Src s); }
                             """;

            var (diags, gen) = GeneratorTestHarness.Run(s);

            Assert.DoesNotContain(diags, d => d.Id == "DWARF008"); // was blocked with empty output pre-fix
            Assert.DoesNotContain(diags, d => d.Id == "DWARF024");
            Assert.Contains("code:", gen, StringComparison.Ordinal); // the WIDE ctor was chosen and fed the rename
        }

        /// <summary>
        ///     R18-31, the selection half: a ctor parameter fed by a dotted source PATH must count as satisfiable.
        ///     Satisfiability is scored before resolution runs, against a flat set of source member names — so a
        ///     path scored as "no source", the narrow ctor was preferred, and the get-only Code member then
        ///     surfaced DWARF008 for a parameter that had a source all along. Same shape as the pair-scoped rename
        ///     above and as ISSUE-044: two places answering "can this parameter be bound?" differently.
        /// </summary>
        [Fact]
        public void Dotted_source_path_into_a_ctor_param_keeps_the_wide_ctor()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Legacy { public int Code { get; set; } }
                             public class Src { public int Id { get; set; } public Legacy Legacy { get; set; } = new(); }
                             public class Dst
                             {
                                 public Dst(int id) { Id = id; }
                                 public Dst(int id, int code) { Id = id; Code = code; }
                                 public int Id { get; }
                                 public int Code { get; }
                             }
                             [DwarfMapper]
                             [MapProperty<Src, Dst>("Legacy.Code", "code")]
                             public partial class M { public partial Dst Map(Src s); }
                             """;

            var (diags, gen) = GeneratorTestHarness.Run(s);

            Assert.DoesNotContain(diags, d => d.Id == "DWARF008");
            Assert.DoesNotContain(diags, d => d.Id == "DWARF024");
            Assert.Contains("code: s.Legacy.Code", gen, StringComparison.Ordinal);
        }

        // ── Round-22 P5 mutation kills ──────────────────────────────────────────────

        /// <summary>
        ///     T3 kill-first #3 — the stated CS1620 invariant: a constructor with ANY ref/out parameter cannot
        ///     be emitted with named arguments. Every pre-existing ref/out test used a ctor whose parameters
        ///     were ALL ref/out, where <c>Any</c> and <c>All</c> agree — this target's only ctor is the mixed
        ///     shape <c>Dst(int a, ref int b)</c>, where the <c>Any → All</c> mutant admits it and the emitted
        ///     call would not compile.
        /// </summary>
        [Fact]
        public void Mixed_ref_and_value_param_ctor_is_unusable_and_reports_DWARF026()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int A { get; set; } public int B { get; set; } }
                             public class Dst
                             {
                                 public Dst(int a, ref int b) { A = a; B = b; }
                                 public int A { get; }
                                 public int B { get; }
                             }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M { }
                             """;

            var (diags, _) = GeneratorTestHarness.Run(s);
            Assert.Contains(diags, d => d.Id == "DWARF026");
        }

        /// <summary>
        ///     The struct explicit-ctor predicate, inaccessibility leg (T3 catalog, ConstructorSelector L55):
        ///     a PRIVATE parameterized ctor must not count as "has an explicit non-parameterless ctor", so the
        ///     implicit zero-init parameterless ctor stays in play and the object-initializer path is used.
        ///     Each <c>&amp;&amp; → ||</c> flip that drops the accessibility (or implicitness) conjunct makes the
        ///     predicate true here, skips the implicit ctor, and lands on DWARF026 instead of clean output.
        /// </summary>
        [Fact]
        public void Struct_with_only_a_private_param_ctor_uses_the_implicit_parameterless_path()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int X { get; set; } }
                             public struct Dst
                             {
                                 private Dst(int x) { X = x; }
                                 public int X { get; set; }
                             }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M { }
                             """;

            var (diags, gen) = GeneratorTestHarness.Run(s);
            Assert.DoesNotContain(diags, d => d.Id == "DWARF026");
            GeneratorAssert.EmitsCompilableCode(s);
            Assert.Contains("new global::Demo.Dst", gen, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The struct explicit-ctor predicate, obsolete leg (same L55 family): an [Obsolete] parameterized
        ///     ctor must not suppress the implicit parameterless path either — the <c>&amp;&amp; → ||</c> flip that
        ///     bypasses the <c>!IsObsolete</c> conjunct reports DWARF026 for a struct that maps cleanly today.
        /// </summary>
        [Fact]
        public void Struct_with_only_an_obsolete_param_ctor_uses_the_implicit_parameterless_path()
        {
            const string s = """
                             using System;
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int X { get; set; } }
                             public struct Dst
                             {
                                 [Obsolete] public Dst(int x) { X = x; }
                                 public int X { get; set; }
                             }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M { }
                             """;

            var (diags, gen) = GeneratorTestHarness.Run(s);
            Assert.DoesNotContain(diags, d => d.Id == "DWARF026");
            Assert.Contains("new global::Demo.Dst", gen, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The optional-parameter exemption in satisfiability scoring (T3 catalog, L230): an author-declared
        ///     default satisfies a parameter the source cannot feed, so the WIDE ctor must stay in the
        ///     satisfiable field and win. Both the <c>continue</c> deletion and the <c>|| → &amp;&amp;</c> flip
        ///     score 'extra' unsatisfiable, silently preferring the narrow ctor — observable because only the
        ///     wide ctor binds 'name'.
        /// </summary>
        [Fact]
        public void Optional_param_with_no_source_keeps_the_wide_ctor_satisfiable()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }
                             public class Dst
                             {
                                 public Dst(int id) { Id = id; }
                                 public Dst(int id, string name, int extra = 5) { Id = id; Name = name; }
                                 public int Id { get; }
                                 public string Name { get; } = "";
                             }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M { }
                             """;

            var (diags, gen) = GeneratorTestHarness.Run(s);
            Assert.DoesNotContain(diags, d => d.Id == "DWARF024");
            Assert.DoesNotContain(diags, d => d.Id == "DWARF026");
            Assert.Contains("name:", gen, StringComparison.Ordinal); // the WIDE ctor was selected
        }

        // ── Direct-call kills for the two selection-time refusals resolution never lets us reach:
        //    an unresolvable [MapProperty] source is DWARF012 long before selection in the real pipeline,
        //    so only a unit call can pin AllParametersHaveASource's own answer (T3 NoCoverage rows). ──

        [Fact]
        public void Unresolvable_dotted_explicit_map_scores_the_ctor_unsatisfiable()
        {
            var (compilation, target, source) = CompileSelectorScenario();

            var diagnostics = new List<DiagnosticInfo>();
            var selected = ConstructorSelector.Select(
                compilation,
                target,
                diagnostics,
                null,
                out _,
                sourceType: source,
                explicitMaps: [("Legacy.Code", "code", null)]); // 'Legacy' is not a member of Src

            Assert.NotNull(selected);
            Assert.Empty(diagnostics);
            // The wide ctor's 'code' param is fed by a dotted path that does NOT resolve → unsatisfiable →
            // the narrow ctor wins. The mutant that returns true on a failed TryResolvePath flips this to
            // the wide ctor.
            Assert.Single(selected.Parameters);
        }

        [Fact]
        public void Explicit_map_naming_a_nonexistent_source_member_scores_the_ctor_unsatisfiable()
        {
            var (compilation, target, source) = CompileSelectorScenario();

            var diagnostics = new List<DiagnosticInfo>();
            var selected = ConstructorSelector.Select(
                compilation,
                target,
                diagnostics,
                null,
                out _,
                sourceType: source,
                explicitMaps: [("Ghost", "code", null)]); // no member 'Ghost' on Src

            Assert.NotNull(selected);
            Assert.Empty(diagnostics);
            Assert.Single(selected.Parameters);
        }

        // ── Round-24 mutation kills: Policy 1's early return, and Policy 0's second answer ───

        /// <summary>
        ///     Policy 1's single-annotated early return. Every pre-existing annotated-constructor fixture puts
        ///     the directive on the constructor the fall-through would have reached anyway - the widest, or the
        ///     only one - so deleting the return changed nothing any assertion looked at. Here the directive
        ///     names the NARROW constructor while a wider one is equally satisfiable: with Policy 1 gone,
        ///     Policy 5's widest-wins rule takes over and the destination is built through
        ///     <c>Dst(int, string)</c>. The annotation would then name one constructor and the mapper would call
        ///     another, which is the one thing <c>[DwarfMapperConstructor]</c> exists to prevent.
        /// </summary>
        [Fact]
        public void An_annotated_narrow_ctor_wins_over_a_wider_satisfiable_one()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int X { get; set; } public string Label { get; set; } = ""; }
                             public class Dst
                             {
                                 [DwarfMapperConstructor] public Dst(int x) { X = x; }
                                 public Dst(int x, string label) { X = x; Label = label; }
                                 public int X { get; }
                                 public string Label { get; set; } = "";
                             }
                             [DwarfMapper]
                             public partial class M { public partial Dst Map(Src s); }
                             """;

            var (diags, gen) = GeneratorTestHarness.Run(s);

            // The annotated ctor IS usable, so nothing was discarded and B31 stays silent.
            Assert.DoesNotContain(diags, d => d.Id == "DWARF098");
            Assert.Contains("x: s.X", gen, StringComparison.Ordinal);
            // 'label' is a constructor parameter ONLY on the wider ctor; Label reaches the destination through
            // the object initializer precisely because the annotated one-argument ctor was honoured.
            Assert.DoesNotContain("label:", gen, StringComparison.Ordinal);
            Assert.Contains("Label = s.Label", gen, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(s);
        }

        /// <summary>
        ///     Policy 0 answers with TWO facts, not one: the constructor to call, and that the destination is
        ///     built by the object-initializer path - the <c>useObjectInitializerOnly</c> out-parameter the
        ///     three extractor call sites branch on. The second fact is invisible downstream: a zero-parameter
        ///     constructor run through <c>ResolveConstructorArguments</c> yields an empty argument set and an
        ///     empty consumed-parameter set, so both branches converge on byte-identical emission and no
        ///     end-to-end assertion can see the flag. (T3's ledger left this open as "a real hole OR a dead
        ///     out-parameter - investigate"; it is neither dead nor emission-visible, it is a contract of this
        ///     method, so it is pinned where it is produced.)
        /// </summary>
        [Fact]
        public void A_parameterless_ctor_is_reported_as_the_object_initializer_path()
        {
            var (compilation, types) = CompileTypes("""
                                                    namespace Demo;
                                                    public class Src { public int Id { get; set; } }
                                                    public class Dst { public Dst() { } public int Id { get; set; } }
                                                    """);

            var diagnostics = new List<DiagnosticInfo>();
            var selected = ConstructorSelector.Select(
                compilation,
                types["Dst"],
                diagnostics,
                null,
                out var useObjectInitializerOnly,
                sourceType: types["Src"]);

            Assert.NotNull(selected);
            Assert.Empty(selected.Parameters);
            Assert.Empty(diagnostics);
            Assert.True(useObjectInitializerOnly,
                "Policy 0 chose the parameterless constructor but did not report the object-initializer path.");
        }

        /// <summary>
        ///     The other half of that contract, and the reason the flag is not simply "the selected constructor
        ///     has no parameters": an ANNOTATED parameterless constructor is returned by the annotation
        ///     override, which leaves the flag false. The projection endpoint documents this exact asymmetry -
        ///     it asks the selected constructor's ARITY rather than the flag, because reading the flag there
        ///     sent it down the constructor path. A flag hard-wired to either constant fails one of these two.
        /// </summary>
        [Fact]
        public void An_annotated_parameterless_ctor_is_not_the_object_initializer_path()
        {
            var (compilation, types) = CompileTypes("""
                                                    namespace DwarfMapper
                                                    {
                                                        public sealed class DwarfMapperConstructorAttribute : System.Attribute { }
                                                    }
                                                    namespace Demo
                                                    {
                                                        public class Src { public int Id { get; set; } }
                                                        public class Dst
                                                        {
                                                            [DwarfMapper.DwarfMapperConstructor]
                                                            public Dst() { }
                                                            public int Id { get; set; }
                                                        }
                                                    }
                                                    """);

            // Fixture premise, asserted so a stub that failed to bind reports itself rather than reporting
            // the selector: this IS the name IsAnnotated compares against.
            var attributeClass = types["Dst"].InstanceConstructors.Single()
                .GetAttributes().Single().AttributeClass;
            Assert.NotNull(attributeClass);
            Assert.Equal("DwarfMapper.DwarfMapperConstructorAttribute", attributeClass.ToDisplayString());

            var diagnostics = new List<DiagnosticInfo>();
            var selected = ConstructorSelector.Select(
                compilation,
                types["Dst"],
                diagnostics,
                null,
                out var useObjectInitializerOnly,
                sourceType: types["Src"]);

            Assert.NotNull(selected);
            Assert.Empty(selected.Parameters);
            Assert.Empty(diagnostics);
            Assert.False(useObjectInitializerOnly,
                "An annotated parameterless constructor is returned by the annotation override, not Policy 0.");
        }

        /// <summary>
        ///     Compiles a standalone snippet and hands back every type it declares, by name. Separate from
        ///     <see cref="CompileSelectorScenario" /> (which is pinned to one two-constructor shape) so a test
        ///     can bring its own destination without disturbing the fixtures already built on that one.
        /// </summary>
        private static (Compilation Compilation, Dictionary<string, INamedTypeSymbol> Types) CompileTypes(
            string code)
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create(
                "SelectorTestAsm_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    tree
                },
                new MetadataReference[]
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
                },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(tree);
            var types = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()
                .Select(d => (INamedTypeSymbol)model.GetDeclaredSymbol(d)!)
                .ToDictionary(t => t.Name, StringComparer.Ordinal);

            return (compilation, types);
        }

        /// <summary>
        ///     Two-ctor Policy-5 scenario for the direct <see cref="ConstructorSelector.Select" /> calls:
        ///     Src{Id}, Dst(int id) / Dst(int id, int code) — no parameterless ctor, nothing annotated.
        /// </summary>
        private static (Compilation Compilation, INamedTypeSymbol Target, INamedTypeSymbol Source)
            CompileSelectorScenario()
        {
            var tree = CSharpSyntaxTree.ParseText("""
                                                  namespace Demo;
                                                  public class Src { public int Id { get; set; } }
                                                  public class Dst
                                                  {
                                                      public Dst(int id) { Id = id; }
                                                      public Dst(int id, int code) { Id = id; Code = code; }
                                                      public int Id { get; }
                                                      public int Code { get; }
                                                  }
                                                  """);
            var compilation = CSharpCompilation.Create(
                "SelectorTestAsm_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    tree
                },
                new MetadataReference[]
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
                },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(tree);
            var types = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()
                .Select(d => (INamedTypeSymbol)model.GetDeclaredSymbol(d)!)
                .ToDictionary(t => t.Name, StringComparer.Ordinal);

            return (compilation, types["Dst"], types["Src"]);
        }

        private static string Internal(string ctorAccessibility, bool flag)
        {
            return OnlyCtor(ctorAccessibility,
                flag,
                ctorAccessibility.Length == 0);
        }

        private static string OnlyCtor(string ctorAccessibility, bool flag, bool extraParameterless = false)
        {
            var acc = ctorAccessibility.Length == 0 ? "internal" : ctorAccessibility;
            var attr = flag ? "[DwarfMapper(AllowNonPublic = true)]" : "[DwarfMapper]";
            return $$"""
                     using DwarfMapper;
                     namespace Demo;
                     public class Src { public int X { get; set; } }
                     public class Dst
                     {
                         {{acc}} Dst() { }
                         public int X { get; set; }
                     }
                     {{attr}}
                     [GenerateMap<Src, Dst>]
                     public partial class M { }
                     """;
        }
    }
}
