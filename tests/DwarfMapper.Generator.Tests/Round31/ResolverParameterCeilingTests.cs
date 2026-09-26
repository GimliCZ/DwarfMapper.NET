// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Generic;
using System.IO;
using System.Linq;
using DwarfMapper.Generator.Tests.Contracts;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     A one-way ratchet on parameter counts in the extraction pipeline: no method may gain parameters, and the
    ///     methods that are already over the ceiling may only lose them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The pipeline threads shared state — the compilation, the policy flags, the accumulators — and the
    ///         cheapest way to reach it from a new helper is to add one more parameter to the chain. Round 30's
    ///         pre-merge review counted the result: methods with seven or more parameters went from 30 to 47 in a
    ///         single round. That is not a style complaint. A twelve-parameter static is where an argument gets
    ///         passed in the wrong position between two same-typed parameters, and the compiler has nothing to say
    ///         about it.
    ///     </para>
    ///     <para>
    ///         So the ceiling is <see cref="Ceiling" /> for anything new, and every method already above it carries
    ///         its measured count as a personal allowance. Both directions are checked, and the second check is the
    ///         one that makes this a ratchet rather than a snapshot: an allowance that a refactor has made slack must
    ///         be lowered or deleted, or the table would quietly re-authorise the parameters that were just removed.
    ///     </para>
    ///     <para>
    ///         <b>Why the table is measured with Roslyn and not with a regex.</b> The round-31 task supplied a
    ///         `(?:private|internal) static` regex to generate it; run against this tree it reported 40 rows, and the
    ///         rows it produced do not cover what this test checks. The test walks every
    ///         <see cref="MethodDeclarationSyntax" /> — public and instance methods included — and a regex also
    ///         mis-parses any signature whose parameter list contains a closing parenthesis. A table built from the
    ///         narrower measurement would leave this test red on an untouched tree, which is the one thing a ratchet
    ///         must never be. The rows below come from the same walk the assertion uses.
    ///     </para>
    /// </remarks>
    public sealed class ResolverParameterCeilingTests
    {
        private const int Ceiling = 6;

        /// <summary>
        ///     Measured on the round-31 tree at the commit that added this test. Rows only ever go DOWN, and
        ///     disappear, as pipeline methods migrate onto the context bundles.
        /// </summary>
        private static readonly Dictionary<string, int> LegacyAllowance = new(StringComparer.Ordinal)
        {
["CollectionConverter.cs::ElementExpr"] = 10,
            ["CollectionConverter.cs::EmitBody"] = 14,
            ["CollectionConverter.cs::Synthesize"] = 11,
            ["CollectionConverter.cs::SynthesizeInPlace"] = 10,
            ["ConstructorSelector.cs::Select"] = 8,
            ["DictionaryConverter.cs::Expr"] = 8,
            ["DictionaryConverter.cs::Synthesize"] = 18,
            ["DictionaryConverter.cs::SynthesizeInPlace"] = 18,
            ["DictionaryConverter.cs::TryResolve"] = 8,
            ["EnumConverter.cs::TryCreate"] = 7,
            ["MapEmitter.cs::EmitDeferredAssignments"] = 7,
            ["MapperExtractor.Conversions.cs::ForgiveConverterNullableReturn"] = 8,
            ["MapperExtractor.Conversions.cs::ForgiveNestedNullableArg"] = 9,
            ["MapperExtractor.Conversions.cs::TryResolveConversion"] = 24,
            ["MapperExtractor.DenseEnum.cs::TryPlanDense"] = 8,
            ["MapperExtractor.DenseEnum.cs::ValidateDenseEnumDirectives"] = 8,
            ["MapperExtractor.Diagnostics.cs::EmitImplicitConversionDiag"] = 8,
            ["MapperExtractor.Flatten.Hetero.cs::ResolveHeterogeneousFlattenGraph"] = 7,
            ["MapperExtractor.Flatten.cs::AppendFlatNodeMemberExpr"] = 7,
            ["MapperExtractor.Flatten.cs::ApplyCollectionKeyUpserts"] = 8,
            ["MapperExtractor.Flatten.cs::CollectReverseRenames"] = 7,
            ["MapperExtractor.Flatten.cs::FlatLeafNeedsBang"] = 8,
            ["MapperExtractor.Flatten.cs::FlatLeafResultNeedsBang"] = 7,
            ["MapperExtractor.Flatten.cs::ResolveFlattenGraphDirectives"] = 17,
            ["MapperExtractor.Flatten.cs::ResolveFlattenInfos"] = 8,
            ["MapperExtractor.Flatten.cs::ResolveUnflattenTarget"] = 23,
            ["MapperExtractor.Flatten.cs::TryResolveSourcePath"] = 7,
            ["MapperExtractor.Hetero.Arms.cs::ResolveDerivedTypeArms"] = 7,
            ["MapperExtractor.Members.cs::ResolveConstructorArguments"] = 21,
            ["MapperExtractor.Members.cs::ResolveMembers"] = 29,
            ["MapperExtractor.Members.cs::TryValidateMapValueTarget"] = 9,
            ["MapperExtractor.Phases.cs::DetectDeclaredMethodsOnRecursionCycle"] = 8,
            ["MapperExtractor.Phases.cs::DrainNestedMappingQueue"] = 8,
            ["MapperExtractor.Phases.cs::ExtractGenerateMapPairs"] = 8,
            ["MapperExtractor.Phases.cs::ProcessDeclaredMethod"] = 7,
            ["MapperExtractor.Phases.cs::ReportCyclicConstructorParameters"] = 8,
            ["MapperExtractor.Phases.cs::ReportSourceMemberCoverage"] = 13,
            ["MapperExtractor.Phases.cs::TryHandleAsyncStreamMap"] = 7,
            ["MapperExtractor.Phases.cs::TryHandleSpanMap"] = 7,
            ["MapperExtractor.Phases.cs::TryHandleUpdateIntoMap"] = 7,
            ["MapperExtractor.Projection.cs::ChooseProjectionConstructor"] = 7,
            ["MapperExtractor.Projection.cs::ResolveProjectionCtorExpr"] = 15,
            ["MapperExtractor.Projection.cs::ResolveProjectionExpr"] = 13,
            ["MapperExtractor.Projection.cs::ResolveProjectionMembers"] = 15,
            ["MapperExtractor.Projection.cs::ResolveProjectionNestedObjectExpr"] = 13,
            ["MapperExtractor.RestatesBase.cs::ReportDrift"] = 7,
            ["MapperExtractor.Share.cs::TryPlanShare"] = 8,
            ["MapperExtractor.cs::EmitSourceCoverage"] = 10,
            ["MapperExtractor.cs::EmitSourceCoverageFromConsumed"] = 9,
            ["MapperExtractor.cs::JudgeUnscopedIgnores"] = 9,
            ["MapperExtractor.cs::ReportDirectivesNotReadHere"] = 7,
            ["MapperExtractor.cs::ReportElementWiseDirectiveGaps"] = 9,
            ["MapperExtractor.cs::ReportUnconsumed"] = 7,
            ["TransferModelShape.cs::TryMeasureMember"] = 10,
        };

        [Fact]
        public void No_pipeline_method_exceeds_its_parameter_allowance()
        {
            var offenders = Measured()
                .Where(kv => kv.Value > (LegacyAllowance.TryGetValue(kv.Key, out var a) ? a : Ceiling))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key} = {kv.Value} > {(LegacyAllowance.TryGetValue(kv.Key, out var a) ? a : Ceiling)}")
                .ToList();

            Assert.True(offenders.Count == 0,
                "Thread shared state through the context bundles instead of adding parameters:\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Legacy_allowances_are_not_stale()
        {
            var actual = Measured();
            var stale = LegacyAllowance
                .Where(kv => !actual.TryGetValue(kv.Key, out var c) || c <= Ceiling || c < kv.Value)
                .Select(kv => actual.TryGetValue(kv.Key, out var c)
                    ? $"{kv.Key}: allowed {kv.Value}, now {c}"
                    : $"{kv.Key}: method no longer exists")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            Assert.True(stale.Count == 0,
                "Lower or delete these allowance rows — leaving them slack re-authorises parameters that have "
                + "already been removed:\n  " + string.Join("\n  ", stale));
        }

        /// <summary>
        ///     Every pipeline method, keyed <c>file::name</c>, carrying its largest parameter count. Overloads and
        ///     partial-file siblings collapse onto one key by design: the key names a method by the two things a
        ///     reader of the allowance table can check by eye, and the maximum is the conservative reading.
        /// </summary>
        private static Dictionary<string, int> Measured()
        {
            var measured = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var file in RepoPaths.SourceFiles(RepoPaths.PipelineDir))
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    var key = Path.GetFileName(file) + "::" + method.Identifier.Text;
                    var count = method.ParameterList.Parameters.Count;
                    if (!measured.TryGetValue(key, out var existing) || count > existing)
                    {
                        measured[key] = count;
                    }
                }
            }

            return measured;
        }
    }
}
