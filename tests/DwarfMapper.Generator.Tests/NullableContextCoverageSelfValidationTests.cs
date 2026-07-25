// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;
using DwarfMapper.Generator.Tests.Fuzzing;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// SELF-VALIDATION: the generative tiers must fuzz the nullable context that consumers actually ship.
/// <para>
/// <c>NullableAnnotation</c> has THREE states — <c>Annotated</c> (<c>string?</c>), <c>NotAnnotated</c>
/// (<c>string</c> under <c>#nullable enable</c>), and <c>None</c> (<i>oblivious</i>: a type written where
/// nullable analysis is off). The generator branches on which one it sees: <c>SourceMayBeNullRef</c>, the
/// null-forgiving <c>!</c> at converter call sites, and DWARF070 all read it.
/// </para>
/// <para>
/// <see cref="GeneratorTestHarness" /> defaults to <c>NullableContextOptions.Disable</c>, so for a long time
/// the entire fuzz + combinatorial corpus produced only the OBLIVIOUS state — while every real consumer builds
/// with <c>&lt;Nullable&gt;enable&lt;/Nullable&gt;</c> (the default for new .NET projects, and this repo's own
/// setting). The generative tiers were therefore exercising code paths production never takes, and skipping
/// the ones it always does. A fuzz suite over the wrong state space goes green no matter what.
/// </para>
/// <para>
/// Asserting on the emitted directive text alone would be a tautology, so this checks the thing that actually
/// matters: that Roslyn, compiling the schema's own output, reports real annotations rather than oblivious
/// ones.
/// </para>
/// </summary>
public class NullableContextCoverageSelfValidationTests
{
    /// <summary>Every reference-type annotation Roslyn reports for the DTOs in a schema-produced source.</summary>
    private static HashSet<NullableAnnotation> ReferenceAnnotationsIn(string source)
    {
        var compilation = GeneratorTestHarness.BuildCompilation("NullCtxCoverage", source);

        var annotations = new HashSet<NullableAnnotation>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var type in tree.GetRoot().DescendantNodes()
                         .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
                         .Select(t => model.GetDeclaredSymbol(t))
                         .OfType<INamedTypeSymbol>())
            foreach (var member in type.GetMembers())
            {
                var memberType = member switch
                {
                    IPropertySymbol p => p.Type,
                    IFieldSymbol f => f.Type,
                    _ => null,
                };

                if (memberType is { IsReferenceType: true })
                    annotations.Add(memberType.NullableAnnotation);
            }
        }

        return annotations;
    }

    [Fact]
    public void The_fuzz_schema_produces_ANNOTATED_reference_members_not_oblivious_ones()
    {
        var annotations = ReferenceAnnotationsIn(SyntheticSchema.Generate(0));

        Assert.Contains(NullableAnnotation.NotAnnotated, annotations);
        Assert.DoesNotContain(NullableAnnotation.None, annotations);
    }

    [Fact]
    public void The_behavioural_fuzz_schema_produces_ANNOTATED_reference_members()
    {
        var annotations = ReferenceAnnotationsIn(SyntheticSchema.GenerateBehavioral(0));

        Assert.Contains(NullableAnnotation.NotAnnotated, annotations);
        Assert.DoesNotContain(NullableAnnotation.None, annotations);
    }

    [Fact]
    public void The_combinatorial_schema_produces_ANNOTATED_reference_members()
    {
        var cell = CombinatorialSchema.DepthOneMatrix().First(c => c.ShapeName == "raw" && c.BasicType == "string");

        var annotations = ReferenceAnnotationsIn(cell.Source);

        Assert.Contains(NullableAnnotation.NotAnnotated, annotations);
        Assert.DoesNotContain(NullableAnnotation.None, annotations);
    }

    [Fact]
    public void The_combinatorial_schema_also_covers_the_NULLABLE_annotation()
    {
        // Both halves of the annotated world have to appear, or the mismatch that DWARF070 exists for
        // (Annotated source -> NotAnnotated target) can never be generated. It previously could not be: the
        // `nullable_ref` shape used string? on BOTH sides, so no cell ever crossed the two.
        var cell = CombinatorialSchema.DepthOneMatrix()
            .First(c => c.ShapeName == "nullable_ref_mismatch");

        var annotations = ReferenceAnnotationsIn(cell.Source);

        Assert.Contains(NullableAnnotation.Annotated, annotations);
        Assert.Contains(NullableAnnotation.NotAnnotated, annotations);
    }
}
