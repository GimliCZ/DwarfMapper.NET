// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Generic;
using System.IO;
using System.Linq;
using DwarfMapper.Generator.Tests.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     Architecture tests for the one property this library's performance story rests on: <b>what the compiler
    ///     decided, the runtime does not re-decide.</b>
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The generator knows every type at the moment it writes the code, and a broken linkage is a build
    ///         failure (<c>DWARF061</c> at the validation root, <c>DWARF005</c> at the pair). So a lookup, a cache or
    ///         a runtime type test in the shipped runtime is, by default, work that a compile-time decision has
    ///         already done — and each one costs more than it looks. A lookup costs time on every call. A cache costs
    ///         mutable static state, an invalidation argument, and — because a cache has no observable behaviour by
    ///         construction — a permanent supply of mutation survivors that no honest test can kill. The round-31
    ///         runtime leg made that concrete: nine of its eleven undetected mutants were cache machinery or guards
    ///         the generator already guarantees.
    ///     </para>
    ///     <para>
    ///         None of these tests forbids runtime resolution. The ambient registry exists because cross-assembly
    ///         dispatch genuinely cannot be decided at the call site, and <c>Map&lt;TDestination&gt;(object)</c>
    ///         genuinely learns its source type from the instance. What they forbid is runtime resolution that
    ///         ARRIVES WITHOUT BEING DECLARED. Each pin below is a list; adding to it is allowed and takes one line
    ///         plus a reason, which is the point — the cost becomes visible in review instead of accumulating.
    ///     </para>
    ///     <para>
    ///         Discovered by scanning 463 generated files and finding zero registry references inside mapper bodies.
    ///         That scan is what these tests lock; it is not repeated here, because a test that depends on
    ///         <c>obj/</c> being populated passes vacuously on a clean tree.
    ///     </para>
    /// </remarks>
    public sealed class RuntimeSurfaceArchitectureTests
    {
        private const string RuntimeDir = "DwarfMapper";

        /// <summary>
        ///     The registry resolution members. Registration members (<c>Register</c>, <c>RegisterUpdate</c>) are
        ///     deliberately absent: those run once from a module initializer, and pinning them would flag startup
        ///     wiring as if it were per-call work.
        /// </summary>
        private static readonly string[] ResolutionCalls =
        [
            "DwarfMapperRegistry.Map(",
            "DwarfMapperRegistry.TryGet(",
            "DwarfMapperRegistry.TryGetUpdate(",
            "DwarfMapperRegistry.Update("
        ];

        // ── 1. The registry is an ENTRY POINT, never reached from inside a mapping ────

        /// <summary>
        ///     Every feature, in one theory: a generated mapper body must never consult the registry. Nesting,
        ///     elements, dictionary values and span fills are all resolved at generation time, so a lookup at any
        ///     depth would mean the generator emitted a question it already knew the answer to — and would put a
        ///     dictionary probe on a per-element path.
        /// </summary>
        /// <remarks>
        ///     <c>GeneratorTestHarness.Run</c> rather than <c>RunAll</c>, and that is the whole mechanism: <c>Run</c>
        ///     excludes the aggregate outputs, so what it returns is mapper bodies only. The registration file is
        ///     SUPPOSED to name the registry on every line; asserting over <c>RunAll</c> would be vacuous.
        /// </remarks>
        [Theory]
        [MemberData(nameof(FeatureShapes))]
        public void A_generated_mapper_body_never_consults_the_registry(string feature, string source)
        {
            var (diagnostics, generated) = GeneratorTestHarness.Run(source);

            Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            Assert.False(string.IsNullOrWhiteSpace(generated), feature + ": the fixture generated nothing, so this would pass vacuously");
            Assert.DoesNotContain("DwarfMapperRegistry", generated, System.StringComparison.Ordinal);
        }

        public static TheoryData<string, string> FeatureShapes()
        {
            const string types = """
                #nullable enable
                using System.Collections.Generic;
                using DwarfMapper;
                namespace Arch;
                public class S { public int V { get; set; } public string? Name { get; set; } }
                public class D { public int V { get; set; } public string? Name { get; set; } }

                """;
            return new TheoryData<string, string>
            {
                { "flat", types + "[DwarfMapper] public partial class M { public partial D Map(S s); }" },
                {
                    "nested", types
                    + "public class NS { public int Id { get; set; } public S Inner { get; set; } = new(); }\n"
                    + "public class ND { public int Id { get; set; } public D Inner { get; set; } = new(); }\n"
                    + "[DwarfMapper] public partial class M { public partial ND Map(NS s); }"
                },
                {
                    "list element", types
                    + "public class LS { public List<S> Items { get; set; } = new(); }\n"
                    + "public class LD { public List<D> Items { get; set; } = new(); }\n"
                    + "[DwarfMapper] public partial class M { public partial LD Map(LS s); }"
                },
                {
                    "array element", types
                    + "public class AS2 { public S[] Items { get; set; } = System.Array.Empty<S>(); }\n"
                    + "public class AD2 { public D[] Items { get; set; } = System.Array.Empty<D>(); }\n"
                    + "[DwarfMapper] public partial class M { public partial AD2 Map(AS2 s); }"
                },
                {
                    "dictionary value", types
                    + "public class DS { public Dictionary<string, S> Map1 { get; set; } = new(); }\n"
                    + "public class DD { public Dictionary<string, D> Map1 { get; set; } = new(); }\n"
                    + "[DwarfMapper] public partial class M { public partial DD Map(DS s); }"
                },
                {
                    "update-into", types
                    + "[DwarfMapper] public partial class M { public partial void Update(S s, D d); }"
                },
                {
                    "span", types
                    + "using System;\n[DwarfMapper] public partial class M { public partial void MapSpan(ReadOnlySpan<S> src, Span<D> dst); }"
                }
            };
        }

        // ── 2. Runtime resolution happens only where it is declared to ───────────────

        /// <summary>
        ///     Every method in the shipped runtime that resolves through the registry, pinned by name. A new one is
        ///     not forbidden — it is required to be declared here, with a reason, in the same commit.
        /// </summary>
        private static readonly Dictionary<string, string> DeclaredResolvers = new(System.StringComparer.Ordinal)
        {
            ["DwarfMapperFacade.Map"] =
                "The ambient entry point, all three overloads. The one-type overload is irreducibly runtime "
                + "(its source type arrives with the instance); the two-type overloads resolve the exact declared "
                + "pair and fall back to it.",
            ["ExactPairSlot.Get"] = "Caches the exact-pair answer for a closed generic pair; see its remarks.",
            ["ExactUpdateSlot.Get"] = "The same for the update-into direction."
        };

        [Fact]
        public void Only_declared_methods_resolve_through_the_registry()
        {
            var found = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var (file, root) in RuntimeSyntax())
            {
                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var text = invocation.Expression.ToString();
                    if (!ResolutionCalls.Any(c => (text + "(").Contains(c, System.StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    found.Add(EnclosingName(invocation, file));
                }
            }

            var undeclared = found.Where(f => !DeclaredResolvers.ContainsKey(f)).ToList();
            Assert.True(undeclared.Count == 0,
                "These methods resolve through the registry at run time and are not declared in "
                + nameof(DeclaredResolvers) + ". The compiler already knows the types at a generated call site, so a "
                + "new runtime lookup needs a reason recorded beside the others:\n  "
                + string.Join("\n  ", undeclared));

            var stale = DeclaredResolvers.Keys.Where(k => !found.Contains(k)).ToList();
            Assert.True(stale.Count == 0,
                "These declarations no longer resolve anything - delete them, or the list stops describing the "
                + "code it exists to constrain:\n  " + string.Join("\n  ", stale));
        }

        // ── 3. Mutable static state is declared ──────────────────────────────────────

        /// <summary>
        ///     Mutable static state in the shipped runtime, pinned. This is the test that catches a CACHE, because a
        ///     cache cannot exist without one — and a cache's machinery is unkillable by mutation testing, so it must
        ///     be a deliberate, reviewed cost rather than something that accumulates.
        /// </summary>
        private static readonly Dictionary<string, string> DeclaredMutableStatics = new(System.StringComparer.Ordinal)
        {
            ["DwarfMapperRegistry._interfaceMaps"] =
                "Copy-on-write array of interface-keyed registrations. Written once per registration from a module "
                + "initializer, read on the ambient interface path; a ConcurrentBag here allocated 56 KB per call.",
            ["DwarfMapperRegistry._version"] =
                "Registration version, read by the slots to invalidate a cached answer.",
            ["ExactPairSlot._entry"] = "The cached (delegate, version) pair for one closed generic pair.",
            ["ExactUpdateSlot._entry"] = "The same for the update-into direction."
        };

        [Fact]
        public void Only_declared_mutable_static_state_exists_in_the_runtime()
        {
            var found = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var (_, root) in RuntimeSyntax())
            {
                foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
                {
                    var mods = field.Modifiers.Select(m => m.ValueText).ToList();
                    if (!mods.Contains("static") || mods.Contains("const") || mods.Contains("readonly"))
                    {
                        continue;
                    }

                    var owner = field.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text
                                ?? "<none>";
                    foreach (var v in field.Declaration.Variables)
                    {
                        found.Add(owner + "." + v.Identifier.Text);
                    }
                }
            }

            var undeclared = found.Where(f => !DeclaredMutableStatics.ContainsKey(f)).ToList();
            Assert.True(undeclared.Count == 0,
                "New mutable static state in the shipped runtime. This is how a cache arrives: it needs somewhere to "
                + "live, its machinery cannot be killed by a mutation test, and it must be invalidated correctly "
                + "forever. Declare it beside the others with what it buys, or resolve the question at compile "
                + "time:\n  " + string.Join("\n  ", undeclared));

            var stale = DeclaredMutableStatics.Keys.Where(k => !found.Contains(k)).ToList();
            Assert.True(stale.Count == 0,
                "These declarations name state that no longer exists - delete the rows:\n  "
                + string.Join("\n  ", stale));
        }

        // ── 4. Runtime type tests are declared ───────────────────────────────────────

        /// <summary>
        ///     Runtime type tests in the shipped runtime, pinned. "The generator already knows the type" is the
        ///     default; each of these is a place where it genuinely does not, because the value arrived through an
        ///     <see cref="object" />-typed registry delegate.
        /// </summary>
        private static readonly Dictionary<string, int> DeclaredTypeTests = new(System.StringComparer.Ordinal)
        {
            ["DwarfCollectionMap"] =
                2, // `source is TSource[]` in ToList and ToArray: the registry delegate is Func<object, object>, so
                   // the concrete shape is not knowable here. An array cannot grow, which is why only it gets the
                   // indexed walk.
            ["DwarfMapperRegistry"] =
                1 // IsInstanceOfType in the interface walk. Inverted on purpose - asking each registered interface
                  // whether it accepts the source keeps the library trim-safe, where GetInterfaces() trips IL2075.
        };

        [Fact]
        public void Only_declared_runtime_type_tests_exist_in_the_runtime()
        {
            var counts = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            foreach (var (_, root) in RuntimeSyntax())
            {
                foreach (var node in root.DescendantNodes())
                {
                    // A runtime type test is one that NAMES A TYPE: `x is Foo f` (declaration pattern), `x is Foo`
                    // (type pattern or the older is-expression), or the reflective pair. Deliberately NOT counted:
                    // `x is null`, `x is not null` and property/list patterns such as `candidates is { Count: > 1 }`
                    // - those inspect a value, not a type, and an earlier version of this test flagged the
                    // ambiguous-interface message's list pattern as if the runtime were re-deciding a type there.
                    var namesAType = node is DeclarationPatternSyntax or TypePatternSyntax
                                     || (node is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.IsExpression));
                    var isReflective = node is InvocationExpressionSyntax inv
                                       && (inv.Expression.ToString().EndsWith("IsInstanceOfType", System.StringComparison.Ordinal)
                                           || inv.Expression.ToString().EndsWith("IsAssignableFrom", System.StringComparison.Ordinal));
                    if (!namesAType && !isReflective)
                    {
                        continue;
                    }

                    var owner = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text;
                    if (owner is null)
                    {
                        continue;
                    }

                    counts[owner] = counts.TryGetValue(owner, out var c) ? c + 1 : 1;
                }
            }

            var problems = new List<string>();
            foreach (var (owner, count) in counts)
            {
                if (!DeclaredTypeTests.TryGetValue(owner, out var allowed))
                {
                    problems.Add(owner + " performs " + count + " runtime type test(s), none declared");
                }
                else if (count != allowed)
                {
                    problems.Add(owner + " performs " + count + " runtime type test(s), " + allowed + " declared");
                }
            }

            problems.AddRange(DeclaredTypeTests.Keys.Where(k => !counts.ContainsKey(k))
                .Select(k => k + " declares runtime type tests it no longer performs"));

            Assert.True(problems.Count == 0,
                "Runtime type tests decide at run time what the generator knew at compile time. Each one needs a "
                + "reason recorded beside the others - the existing two are there because the value arrives through "
                + "an object-typed registry delegate:\n  " + string.Join("\n  ", problems));
        }

        // ── 5. EVERY public API declares how it behaves at run time ──────────────────

        /// <summary>
        ///     The runtime behaviour class of every public type in the shipped assembly. Driven off
        ///     <c>PublicAPI.Shipped.txt</c> and <c>PublicAPI.Unshipped.txt</c>, so a new public type cannot appear
        ///     without a decision being made about it.
        /// </summary>
        /// <remarks>
        ///     This is the general form of the question, and the reason it is not one test about one method: the
        ///     property wanted is not "the facade is fast", it is "no public entry point re-decides at run time what
        ///     the generator decided at compile time". Stated per type, an API that quietly adds a lookup fails here
        ///     rather than being noticed two rounds later in a mutation report.
        /// </remarks>
        private static readonly string[] RuntimeResolutionTypes =
        [
            // The ambient entry point and its backing table. These MAY resolve at run time - it is their purpose,
            // and only theirs: a cross-assembly call site genuinely cannot know, and Map<TDestination>(object)
            // learns its source type from the instance.
            "DwarfMapperRegistry", "DwarfMapperFacade", "IDwarfMapper"
        ];

        private static readonly string[] GeneratedCodeSupportTypes =
        [
            // Called BY generated code, with every type already decided by the generator. They may test a type only
            // where the value arrived through an object-typed registry delegate, and they may never resolve a map.
            "DwarfCollectionMap", "DwarfRefContext"
        ];

        private static readonly string[] DiagnosticTypes =
        [
            // Thrown at run time, but they resolve nothing.
            "DwarfMapMissingException", "DwarfMapValidationException", "DwarfMappingDepthException"
        ];

        private static readonly string[] CompileTimeDirectiveTypes =
        [
            // Attributes, enums and the fluent config DSL: the generator READS these, the runtime never executes
            // them. MapConfig belongs here rather than with the support types - the generator reads its method
            // bodies syntactically and never calls them, which is why its members are expression-bodied (round 24:
            // making them blocks minted ten NoCoverage mutants in code no test can reach).
            "AfterMapAttribute", "AutoNestAttribute", "BeforeMapAttribute", "DwarfMapperAttribute",
            "DwarfMapperConstructorAttribute", "DwarfMapperDefaultsAttribute", "DwarfMapperOptionsAttribute",
            "DwarfMapperValidationRootAttribute", "DwarfProvidesMapAttribute", "DwarfRequiresMapAttribute",
            "EnumStrategy", "EnumStringSource", "FlattenAttribute", "FlattenGraphAttribute",
            "GenerateMapAttribute", "GenerateWrapperMapAttribute", "MapCollectionKeyAttribute", "MapConfig",
            "MapConstructorAttribute", "MapDenseEnumKeysAttribute", "MapDerivedTypeAttribute", "MapIgnoreAttribute",
            "MapIgnoreSourceAttribute", "MapNullSkipAttribute", "MapPropertyAttribute", "MapShareAttribute",
            "MapToAttribute", "MapValueAttribute", "NameConvention", "NullCollectionStrategy", "NullStrategy",
            "OnCycleStrategy", "ProvidesMapAttribute", "ReferenceHandlingStrategy", "ReinterpretAttribute",
            "RequiredMappingStrategy", "RestatesBaseAttribute", "ReverseMapAttribute", "RoundTripAttribute",
            "UsesMapAttribute"
        ];

        [Fact]
        public void Every_public_type_declares_how_it_behaves_at_run_time()
        {
            var classified = new Dictionary<string, string>(System.StringComparer.Ordinal);
            foreach (var (bucket, names) in new (string Bucket, string[] Names)[]
                     {
                         ("runtime-resolution", RuntimeResolutionTypes),
                         ("generated-code support", GeneratedCodeSupportTypes),
                         ("diagnostic", DiagnosticTypes),
                         ("compile-time directive", CompileTimeDirectiveTypes)
                     })
            {
                foreach (var n in names)
                {
                    Assert.False(classified.ContainsKey(n), n + " is classified twice");
                    classified[n] = bucket;
                }
            }

            var declared = PublicTypeNames();
            var unclassified = declared.Where(t => !classified.ContainsKey(t)).ToList();
            Assert.True(unclassified.Count == 0,
                "New public API, unclassified. Decide what it does at RUN TIME before it ships: a compile-time "
                + "directive the generator reads, support called by generated code with the types already decided, a "
                + "diagnostic, or genuine runtime resolution - and the last needs a reason no compile-time decision "
                + "can satisfy:\n  " + string.Join("\n  ", unclassified));

            var vanished = classified.Keys.Where(t => !declared.Contains(t)).ToList();
            Assert.True(vanished.Count == 0,
                "These types are classified but are no longer public API - delete the rows:\n  "
                + string.Join("\n  ", vanished));

            // The pin that matters: the set allowed to resolve at run time does not grow quietly.
            Assert.Equal(3, RuntimeResolutionTypes.Length);
        }

        [Fact]
        public void No_public_type_outside_the_ambient_entry_point_resolves_at_run_time()
        {
            var offenders = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var (file, root) in RuntimeSyntax())
            {
                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var text = invocation.Expression.ToString() + "(";
                    if (!ResolutionCalls.Any(c => text.Contains(c, System.StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    var owner = invocation.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text
                                ?? Path.GetFileNameWithoutExtension(file);
                    var allowed = RuntimeResolutionTypes.Contains(owner, System.StringComparer.Ordinal)
                                  || owner.StartsWith("Exact", System.StringComparison.Ordinal);
                    if (!allowed)
                    {
                        offenders.Add(owner + " (" + file + ")");
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "A type outside the ambient entry point resolves a map at run time. The generator knows the types at "
                + "every generated call site, and DWARF061/DWARF005 already fail the build when a linkage is broken, "
                + "so this is work a compile-time decision has already done:\n  " + string.Join("\n  ", offenders));
        }

        private static SortedSet<string> PublicTypeNames()
        {
            var names = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var f in new[] { "PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt" })
            {
                var path = Path.Combine(RepoPaths.Src, RuntimeDir, f);
                if (!File.Exists(path))
                {
                    continue;
                }

                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#'))
                    {
                        continue;
                    }

                    var head = line.Split(" -> ")[0];
                    foreach (var prefix in new[] { "static ", "virtual ", "override ", "abstract ", "sealed " })
                    {
                        if (head.StartsWith(prefix, System.StringComparison.Ordinal))
                        {
                            head = head[prefix.Length..];
                        }
                    }

                    if (!head.StartsWith("DwarfMapper.", System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var type = head["DwarfMapper.".Length..].Split('(')[0].Split('.')[0];
                    var generic = type.IndexOf('<', System.StringComparison.Ordinal);
                    names.Add(generic > 0 ? type[..generic] : type);
                }
            }

            Assert.True(names.Count > 20,
                "the public API files parsed to almost nothing, so this would pass vacuously");
            return names;
        }

        private static IEnumerable<(string File, SyntaxNode Root)> RuntimeSyntax()
        {
            var dir = Path.Combine(RepoPaths.Src, RuntimeDir);
            foreach (var file in RepoPaths.SourceFiles(dir))
            {
                yield return (Path.GetFileName(file), CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot());
            }
        }

        private static string EnclosingName(SyntaxNode node, string file)
        {
            var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            var type = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            var typeName = type?.Identifier.Text ?? Path.GetFileNameWithoutExtension(file);
            return typeName + "." + (method?.Identifier.Text ?? "<initializer>");
        }
    }
}
