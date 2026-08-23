// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.CompilerTests.TypeGraphs;
using DwarfMapper.Generator;
using DwarfMapper.Generator.Tests.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TypeKind = DwarfMapper.CompilerTests.TypeGraphs.TypeKind;

namespace DwarfMapper.CompilerTests
{
    /// <summary>
    ///     The RFC's companion scan, audit-endorsed: the descriptor's <see cref="TypeKind" /> /
    ///     <see cref="MemberShape" /> / <see cref="CollShape" /> enums must cover every kind and shape the
    ///     surface matrix's case-space enumerates — so a generator blind spot is a DECLARED, exactly pinned
    ///     population rather than a silent one (the YARPGen "generator bias caps yield" lesson as a test, the
    ///     same family as P6's excuse obligations).
    ///     <para>
    ///         Direction: descriptor ⊇ case-space. The hand-built matrix is the floor — the generated space
    ///         must be able to express at least every kind the matrix already exercises by hand. The matrix
    ///         does not (yet) demand <see cref="TypeKind.Record" /> or <see cref="TypeKind.RecordStruct" />;
    ///         those are pinned by the sampled space itself (the smoke rolls them) and by K2's MR-3, not here.
    ///     </para>
    ///     <para>
    ///         Sources of authority, per axis: kinds and member shapes are parsed out of the case-space's
    ///         fixture corpus (every string-literal C# fragment under Generator.Tests/Contracts — "the fixtures
    ///         are C# and the test project already has Roslyn, so the question is answered exactly", per
    ///         SurfaceFixtures' own words); collection shapes are read from the generator's OWN taxonomy enums
    ///         reflectively, the stronger authority, exactly as CollectionCoverageSelfValidationTests reads
    ///         them (the type is internal, and a copied list is exactly what drifts).
    ///     </para>
    /// </summary>
    public class EnumCoverageRatchetTests
    {
        // ── The case-space fixture corpus, parsed ────────────────────────────────

        private static readonly Regex DeclarationHint =
            new(@"\b(?:class|struct|record|enum|interface)\s", RegexOptions.Compiled);

        // ── Axis 1: declaration kinds ────────────────────────────────────────────

        /// <summary>
        ///     Kinds the case-space declares that the DESCRIPTOR deliberately does not roll as graph nodes,
        ///     with the reason. Exactly pinned in both directions: an entry that stops appearing in the corpus
        ///     is stale (delete it here), a new unmapped kind fails the coverage assertion until it is either
        ///     added to <see cref="TypeKind" /> or declared here with a reason.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> DeclaredKindGaps =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Enum"] = "enum declarations enter mappings as MEMBER types, not graph nodes; the descriptor's " + "scalar vocabulary does not yet roll enum types — a declared blind spot for a " + "later K-task, not a silent one"
            };

        // ── Axis 2: member shapes ────────────────────────────────────────────────

        /// <summary>
        ///     Same contract as <see cref="DeclaredKindGaps" />, for member shapes. "GetOnly" maps to
        ///     <see cref="MemberShape.CtorParam" /> rather than appearing here: the descriptor's rendering of a
        ///     ctor-assigned member IS a get-only auto-property, so the shape is covered, not exempt.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> DeclaredShapeGaps =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Empty as of 2026-08-22: every member shape the case-space declares maps onto MemberShape
                // (measured — the scan's own stale-row check deleted a speculative "Computed" row on first
                // run, because the corpus declares no expression-bodied properties).
            };

        // ── Axis 3: collection shapes vs the generator's own taxonomy ────────────

        /// <summary>
        ///     Supported-by-the-generator collection targets the descriptor does not yet roll. EXACTLY pinned
        ///     and shrink-only: adding a CollShape member removes its row here in the same commit; a NEW
        ///     supported target fails the coverage assertion until it is rolled or declared. The pin is the
        ///     teeth — the blind spot can only shrink, never silently grow.
        /// </summary>
        private static readonly HashSet<string> DeclaredCollectionGaps = new(StringComparer.Ordinal)
        {
            // TargetKind (CollectionConverter)
            "Queue",
            "Stack",
            "IEnumerable",
            "ICollection",
            "IList",
            "IReadOnlyCollection",
            "ISet",
            "IReadOnlySet",
            "ImmutableArray",
            "ImmutableList",
            "IImmutableList",
            "ImmutableHashSet",
            "IImmutableSet",
            // DictTargetKind (DictionaryConverter)
            "IDictionary",
            "IReadOnlyDictionary",
            "ImmutableDictionary",
            "IImmutableDictionary"
        };

        /// <summary>
        ///     Every C# fragment embedded as a string literal in the Contracts folder — the surface matrix's
        ///     fixtures, endpoint templates and probe shapes. Parsing arbitrary non-C# literals is harmless
        ///     (they contribute no type declarations); the hint regex just keeps the parse count small.
        ///     Termination (H7): bounded by file count × token count, no recursion.
        /// </summary>
        private static List<CompilationUnitSyntax> CaseSpaceFragments()
        {
            var contractsDir = Path.Combine(RepoPaths.Tests, "DwarfMapper.Generator.Tests", "Contracts");
            var fragments = new List<CompilationUnitSyntax>();
            foreach (var file in RepoPaths.SourceFiles(contractsDir))
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
                foreach (var token in root.DescendantTokens())
                {
                    if (token.Kind() is not (SyntaxKind.StringLiteralToken
                        or SyntaxKind.SingleLineRawStringLiteralToken
                        or SyntaxKind.MultiLineRawStringLiteralToken))
                    {
                        continue;
                    }

                    if (!DeclarationHint.IsMatch(token.ValueText))
                    {
                        continue;
                    }

                    fragments.Add(SyntaxFactory.ParseCompilationUnit(token.ValueText));
                }
            }

            Assert.True(fragments.Count > 0,
                "no C# fragments found in the Contracts folder — the case-space moved and this scan is " + "pointed at nothing; fix the path, do not let the ratchet go vacuous.");
            return fragments;
        }

        [Fact]
        public void TypeKind_covers_every_declaration_kind_the_case_space_enumerates()
        {
            var found = CaseSpaceFragments()
                .SelectMany(f => f.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                .Select(KindLabel)
                .ToHashSet(StringComparer.Ordinal);

            var unmapped = found
                .Where(label => !Enum.TryParse<TypeKind>(label, out _))
                .Where(label => !DeclaredKindGaps.ContainsKey(label))
                .Order(StringComparer.Ordinal)
                .ToList();
            Assert.True(unmapped.Count == 0,
                "The surface matrix's case-space declares kinds the descriptor can neither roll nor account " + "for. Add them to TypeKind (and to the generators + renderer), or declare the gap with a " + "reason in DeclaredKindGaps:\n  " + string.Join("\n  ", unmapped));

            var stale = DeclaredKindGaps.Keys.Where(k => !found.Contains(k)).Order(StringComparer.Ordinal).ToList();
            Assert.True(stale.Count == 0,
                "Declared kind gaps no longer present in the case-space — a stale excuse is an allowlist; " + "delete these rows:\n  " + string.Join("\n  ", stale));
        }

        private static string KindLabel(BaseTypeDeclarationSyntax declaration)
        {
            return declaration switch
            {
                RecordDeclarationSyntax r =>
                    r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "RecordStruct" : "Record",
                ClassDeclarationSyntax => "Class",
                StructDeclarationSyntax => "Struct",
                InterfaceDeclarationSyntax => "Interface",
                EnumDeclarationSyntax => "Enum",
                _ => declaration.Kind().ToString()
            };
        }

        [Fact]
        public void MemberShape_covers_every_member_shape_the_case_space_enumerates()
        {
            var found = CaseSpaceFragments()
                .SelectMany(ShapeLabels)
                .ToHashSet(StringComparer.Ordinal);
            Assert.True(found.Count > 0, "the case-space fragments declared no members at all — scan is vacuous");

            var unmapped = found
                .Select(label => label == "GetOnly" ? nameof(MemberShape.CtorParam) : label)
                .Where(label => !Enum.TryParse<MemberShape>(label, out _))
                .Where(label => !DeclaredShapeGaps.ContainsKey(label))
                .Order(StringComparer.Ordinal)
                .ToList();
            Assert.True(unmapped.Count == 0,
                "The case-space declares member shapes the descriptor can neither roll nor account for. Add " + "them to MemberShape (and the renderer), or declare the gap with a reason in " + "DeclaredShapeGaps:\n  " + string.Join("\n  ", unmapped));

            var stale = DeclaredShapeGaps.Keys.Where(k => !found.Contains(k)).Order(StringComparer.Ordinal).ToList();
            Assert.True(stale.Count == 0,
                "Declared shape gaps no longer present in the case-space — delete these stale rows:\n  " + string.Join("\n  ", stale));
        }

        private static IEnumerable<string> ShapeLabels(CompilationUnitSyntax fragment)
        {
            foreach (var node in fragment.DescendantNodes())
                switch (node)
                {
                    case PropertyDeclarationSyntax p when p.ExpressionBody is not null:
                        yield return "Computed";
                        break;

                    case PropertyDeclarationSyntax p when p.Modifiers.Any(SyntaxKind.RequiredKeyword):
                        yield return "Required";
                        break;

                    case PropertyDeclarationSyntax p
                        when p.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.InitAccessorDeclaration)) == true:
                        yield return "InitOnly";
                        break;

                    case PropertyDeclarationSyntax p
                        when p.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)) == true:
                        yield return "AutoProp";
                        break;

                    case PropertyDeclarationSyntax:
                        yield return "GetOnly";
                        break;

                    case FieldDeclarationSyntax f when !f.Modifiers.Any(SyntaxKind.ConstKeyword):
                        yield return "Field";
                        break;

                    case ConstructorDeclarationSyntax c when c.ParameterList.Parameters.Count > 0:
                        yield return "CtorParam";
                        break;
                }
        }

        [Fact]
        public void CollShape_covers_the_generator_taxonomy_or_declares_the_exact_blind_spot()
        {
            var supported = SupportedTaxonomy();
            var rolled = Enum.GetNames<CollShape>().Where(n => n != nameof(CollShape.None))
                .ToHashSet(StringComparer.Ordinal);

            // Every rolled shape must BE a supported target under the same name — the descriptor must not
            // invent collection shapes the product does not claim (that would fuzz the wrong product).
            var invented = rolled.Where(r => !supported.Contains(r)).Order(StringComparer.Ordinal).ToList();
            Assert.True(invented.Count == 0,
                "CollShape rolls collection targets the generator taxonomy does not name:\n  " + string.Join("\n  ", invented));

            // Supported = rolled ∪ declared, EXACTLY, and the two halves are disjoint.
            var silent = supported.Where(s => !rolled.Contains(s) && !DeclaredCollectionGaps.Contains(s))
                .Order(StringComparer.Ordinal).ToList();
            Assert.True(silent.Count == 0,
                "The generator supports collection targets the descriptor neither rolls nor declares — a " + "SILENT generator-bias blind spot (this is exactly how the IEnumerable<T> aliasing bug hid " + "from the fuzzers). Roll them in CollShape or declare them in DeclaredCollectionGaps:\n  " + string.Join("\n  ", silent));

            var overlap = DeclaredCollectionGaps.Where(rolled.Contains).Order(StringComparer.Ordinal).ToList();
            Assert.True(overlap.Count == 0,
                "These targets are BOTH rolled and declared-as-gap — remove the stale gap rows (shrink-only, " + "same commit as the CollShape addition):\n  " + string.Join("\n  ", overlap));

            var stale = DeclaredCollectionGaps.Where(g => !supported.Contains(g)).Order(StringComparer.Ordinal).ToList();
            Assert.True(stale.Count == 0,
                "Declared gaps that are no longer in the generator's taxonomy — delete the stale rows:\n  " + string.Join("\n  ", stale));
        }

        /// <summary>
        ///     The generator's own collection + dictionary taxonomies, read reflectively — the same mechanism,
        ///     for the same reason, as CollectionCoverageSelfValidationTests: the enums are internal and a
        ///     copied list is exactly what drifts.
        /// </summary>
        private static HashSet<string> SupportedTaxonomy()
        {
            var generatorAssembly = typeof(DwarfGenerator).Assembly;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var enumName in new[]
                     {
                         "DwarfMapper.Generator.Pipeline.CollectionConverter+TargetKind", "DwarfMapper.Generator.Pipeline.DictionaryConverter+DictTargetKind"
                     })
            {
                var type = generatorAssembly.GetType(enumName);
                Assert.True(type is not null,
                    $"{enumName} not found — the taxonomy moved; this ratchet must be repointed, not deleted.");
                names.UnionWith(Enum.GetNames(type));
            }

            return names;
        }
    }
}
