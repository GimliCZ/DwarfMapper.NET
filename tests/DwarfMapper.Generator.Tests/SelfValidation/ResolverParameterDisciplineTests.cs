// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Contracts;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     ARCHITECTURAL RULE (REG-02, proposed round 19, built round 27): a generator method may not declare an
    ///     OPTIONAL parameter whose name is in the option vocabulary — the options that gate behaviour.
    ///     <para>
    ///         ISSUE-043 and ISSUE-044 were the same bug twice: <i>an optional parameter defaults to the
    ///         permissive value, and one call site forgets to pass it.</i> Nothing fails; the generator simply
    ///         behaves as though the consumer had asked for the permissive thing. The fix made the offending
    ///         parameters required, which is why <c>caseInsensitive</c> is required today.
    ///     </para>
    ///     <para>
    ///         REG-02 was written to make the CLASS unreintroducible, and then never built. Measured 2026-08-26,
    ///         at the commit that adds this file: <b>21 of <c>ResolveMembers</c>' 36 parameters carry defaults</b>,
    ///         including <c>autoNest</c>, <c>allowNonPublic</c>, <c>explicitOnly</c> and <c>ignoreObsolete</c> —
    ///         four of the six names REG-02 itself listed. The fix had been applied to one parameter and the
    ///         guard that would have generalised it existed only as prose. That is the
    ///         "a fix applied to 1 of N identical construction sites" shape this repository has found before.
    ///     </para>
    ///     <para>
    ///         The allowlist is SHRINK-ONLY and pinned at the measured count. It empties as the round-27
    ///         <c>ExtractionContext</c> migration threads these options through a context object instead: a
    ///         context has exactly one construction site per extraction, so every option is decided once and the
    ///         compiler enforces that it was decided at all.
    ///     </para>
    /// </summary>
    public class ResolverParameterDisciplineTests
    {
        /// <summary>
        ///     The option vocabulary: names whose default silently selects a behaviour. Two named in REG-02's
        ///     proposal are absent because no parameter carries them today; the rest were measured.
        /// </summary>
        private static readonly HashSet<string> OptionVocabulary = new(StringComparer.Ordinal)
        {
            "autoNest",
            "allowNonPublic",
            "explicitOnly",
            "ignoreObsolete",
            "caseInsensitive",
            "implicitConversions",
            "nullAsNull",
            "isPreserve",
            "isSetNull",
            "skipNullSourceMembers",
            "requiredMembersAlreadySatisfied"
        };

        /// <summary>
        ///     Measured offenders at the commit that introduced this scan. SHRINK-ONLY: a row may be deleted when
        ///     the parameter becomes required or moves into the context, never added. Pinning the measurement
        ///     rather than the aspiration is what lets this land without a refactor in the same commit.
        /// </summary>
        private static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
        {
            // 14 rows. Was 31 when this scan was written. ResolveMembers lost nine and ResolveProjectionMembers seven when
            // R27-02 bundled their mapper-wide flags into MapperOptions — the allowlist doing exactly what a
            // shrink-only list is for. TryResolveConversion's autoNest became required in round 30, when the
            // non-nullable nestedRegistry after it could no longer carry a default. What remains is the converters
            // and the constructor-argument resolver.
            "CollectionConverter.cs::Synthesize::isPreserve",
            "CollectionConverter.cs::TryResolve::nullAsNull",
            "DictionaryConverter.cs::Synthesize::isPreserve",
            "DictionaryConverter.cs::Synthesize::nullAsNull",
            "MapperExtractor.Conversions.cs::TryResolveConversion::implicitConversions",
            "MapperExtractor.Conversions.cs::TryResolveConversion::isPreserve",
            "MapperExtractor.Conversions.cs::TryResolveConversion::isSetNull",
            "MapperExtractor.Conversions.cs::TryResolveConversion::nullAsNull",
            "MapperExtractor.Members.cs::ResolveConstructorArguments::implicitConversions",
            "MapperExtractor.Members.cs::ResolveConstructorArguments::isPreserve",
            "MapperExtractor.Members.cs::ResolveConstructorArguments::isSetNull",
            "MapperExtractor.Members.cs::ResolveConstructorArguments::nullAsNull",
            "MapperExtractor.Members.cs::ResolveMembers::requiredMembersAlreadySatisfied",
            "NestedMappingRegistry.cs::GetOrReserve::autoNest"
        };

        /// <summary>
        ///     ARCHITECTURAL RULE (REG-02's DUAL, proposed round 26 as R26-02, built round 30): a generator
        ///     method may not declare more than <see cref="ParameterCeiling" /> parameters.
        ///     <para>
        ///         REG-02 above bans an OPTIONAL option parameter, because a default that one call site forgets
        ///         is ISSUE-043 and ISSUE-044. Making those parameters REQUIRED fixed that class and bought the
        ///         next one: totality paid for in parameter width. A 29-parameter call is not type-checked in any
        ///         way a reader can verify — a transposed pair of same-typed arguments compiles clean, and no
        ///         test distinguishes it from the correct order. The two rules are the same rule seen from two
        ///         sides: state must be threaded EXPLICITLY, and threading it as a parameter list stops being
        ///         explicit somewhere around the seventh argument.
        ///     </para>
        ///     <para>
        ///         MEASURED at the commit that adds this scan, over <c>src/DwarfMapper.Generator</c>: 611 methods,
        ///         of which <b>64 declare more than six parameters</b> — 63 of them in <c>Pipeline/</c>. The worst
        ///         are <c>ResolveMembers</c> at <b>29</b>, <c>TryResolveConversion</c> at 24,
        ///         <c>ResolveUnflattenTarget</c> at 23 and <c>ResolveConstructorArguments</c> at 21. The round-30
        ///         external audit reported this class as "47 methods at ≥7 params, worst 16"; measured with this
        ///         scan the count at ≥7 is 64 (46 at ≥8, which is probably the figure it meant) and the worst is
        ///         29, not 16. The audit's finding was right and its numbers were low — recorded here because a
        ///         ratchet pinned to someone else's arithmetic is not a measurement.
        ///     </para>
        ///     <para>
        ///         The allowance below is pinned at EXACT current counts, not at "measured + headroom". Headroom
        ///         is the slack a threading bug hides in: with a ceiling of 30 on a 29-parameter method, the
        ///         thirtieth parameter lands silently. Every row is therefore a debt with a number on it, and the
        ///         R26-02 <c>ExtractionContext</c> migration deletes rows rather than editing them — a method that
        ///         drops to six parameters or fewer leaves this table entirely.
        ///     </para>
        ///     <para>
        ///         Local functions are scanned too, unlike REG-02's walk. A resolver extracted into a local
        ///         function is the same hazard wearing a different syntax node, and leaving them out would make
        ///         the ceiling avoidable by refactoring rather than by fixing. None exceeds the ceiling today.
        ///     </para>
        /// </summary>
        private const int ParameterCeiling = 6;

        /// <summary>
        ///     Measured offenders at the commit that introduced this scan, as exact <c>file::method = count</c>
        ///     rows. SHRINK-ONLY, and the row carries its COUNT so growth is caught as well as arrival: a method
        ///     that gains a parameter no longer matches its row and fails as a new offender.
        ///     <para>
        ///         Keyed by text rather than by a dictionary so OVERLOADS work: <c>DictionaryConverter.TryResolve</c>
        ///         appears twice, at 8 and at 7, and a map keyed by method name would have silently kept only one
        ///         of them and stopped watching the other.
        ///     </para>
        /// </summary>
        private static readonly HashSet<string> ParameterAllowance = new(StringComparer.Ordinal)
        {
            "MapperExtractor.Members.cs::ResolveMembers = 29",
            "MapperExtractor.Conversions.cs::TryResolveConversion = 24",
            "MapperExtractor.Flatten.cs::ResolveUnflattenTarget = 23",
            "MapperExtractor.Members.cs::ResolveConstructorArguments = 21",
            "DictionaryConverter.cs::Synthesize = 18",
            "DictionaryConverter.cs::SynthesizeInPlace = 18",
            "MapperExtractor.Flatten.cs::ResolveFlattenGraphDirectives = 17",
            "MapperExtractor.Projection.cs::ResolveProjectionCtorExpr = 15",
            "MapperExtractor.Projection.cs::ResolveProjectionMembers = 15",
            "CollectionConverter.cs::EmitBody = 14",
            "MapperExtractor.Phases.cs::ReportSourceMemberCoverage = 13",
            "MapperExtractor.Projection.cs::ResolveProjectionExpr = 13",
            "MapperExtractor.Projection.cs::ResolveProjectionNestedObjectExpr = 13",
            "CollectionConverter.cs::EmitArray = 11",
            "CollectionConverter.cs::EmitHashSet = 11",
            "CollectionConverter.cs::EmitList = 11",
            "CollectionConverter.cs::Synthesize = 11",
            "CollectionConverter.cs::ElementExpr = 10",
            "CollectionConverter.cs::EmitImmutableCollection = 10",
            "CollectionConverter.cs::EmitLazyEnumerable = 10",
            "CollectionConverter.cs::EmitStackQueue = 10",
            "CollectionConverter.cs::SynthesizeInPlace = 10",
            "MapperExtractor.cs::EmitSourceCoverage = 10",
            "TransferModelShape.cs::TryMeasureMember = 10",
            "CollectionConverter.cs::EmitImmutableArray = 9",
            "MapperExtractor.Conversions.cs::ForgiveNestedNullableArg = 9",
            "MapperExtractor.Members.cs::TryValidateMapValueTarget = 9",
            "MapperExtractor.cs::EmitSourceCoverageFromConsumed = 9",
            "MapperExtractor.cs::JudgeUnscopedIgnores = 9",
            "MapperExtractor.cs::ReportElementWiseDirectiveGaps = 9",
            "AggregateEmitter.cs::ExtCandidate = 8",
            "ConstructorSelector.cs::Select = 8",
            "DictionaryConverter.cs::Expr = 8",
            "DictionaryConverter.cs::TryResolve = 8",
            "MapperExtractor.Conversions.cs::ForgiveConverterNullableReturn = 8",
            "MapperExtractor.DenseEnum.cs::TryPlanDense = 8",
            "MapperExtractor.DenseEnum.cs::ValidateDenseEnumDirectives = 8",
            "MapperExtractor.Diagnostics.cs::EmitImplicitConversionDiag = 8",
            "MapperExtractor.Flatten.cs::ApplyCollectionKeyUpserts = 8",
            "MapperExtractor.Flatten.cs::FlatLeafNeedsBang = 8",
            "MapperExtractor.Flatten.cs::ResolveFlattenInfos = 8",
            "MapperExtractor.Phases.cs::DetectDeclaredMethodsOnRecursionCycle = 8",
            "MapperExtractor.Phases.cs::DrainNestedMappingQueue = 8",
            "MapperExtractor.Phases.cs::ExtractGenerateMapPairs = 8",
            "MapperExtractor.Phases.cs::ReportCyclicConstructorParameters = 8",
            "MapperExtractor.Share.cs::TryPlanShare = 8",
            "DictionaryConverter.cs::TryResolve = 7",
            "EnumConverter.cs::TryCreate = 7",
            "MapEmitter.cs::EmitDeferredAssignments = 7",
            "MapperExtractor.Flatten.Hetero.cs::ResolveHeterogeneousFlattenGraph = 7",
            "MapperExtractor.Flatten.cs::AppendFlatNodeMemberExpr = 7",
            "MapperExtractor.Flatten.cs::CollectReverseRenames = 7",
            "MapperExtractor.Flatten.cs::FlatLeafResultNeedsBang = 7",
            "MapperExtractor.Flatten.cs::TryResolveSourcePath = 7",
            "MapperExtractor.Hetero.Arms.cs::ResolveDerivedTypeArms = 7",
            "MapperExtractor.Phases.cs::ProcessDeclaredMethod = 7",
            "MapperExtractor.Phases.cs::TryHandleAsyncStreamMap = 7",
            "MapperExtractor.Phases.cs::TryHandleSpanMap = 7",
            "MapperExtractor.Phases.cs::TryHandleUpdateIntoMap = 7",
            "MapperExtractor.Projection.cs::ChooseProjectionConstructor = 7",
            "MapperExtractor.RestatesBase.cs::ReportDrift = 7",
            "MapperExtractor.cs::ReportDirectivesNotReadHere = 7",
            "MapperExtractor.cs::ReportUnconsumed = 7",
            "MemberFacts.cs::TryResolvePath = 7"
        };

        private static List<string> FindMethodsOverTheCeiling()
        {
            var found = new List<string>();

            foreach (var path in RepoPaths.SourceFiles(RepoPaths.GeneratorSrcDir))
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
                var file = Path.GetFileName(path);

                foreach (var method in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
                {
                    var name = method switch
                    {
                        MethodDeclarationSyntax m => m.Identifier.ValueText,
                        ConstructorDeclarationSyntax c => c.Identifier.ValueText,
                        _ => null
                    };

                    if (name is null) { continue; }

                    if (method.ParameterList.Parameters.Count > ParameterCeiling)
                    {
                        found.Add($"{file}::{name} = {method.ParameterList.Parameters.Count}");
                    }
                }

                // A local function is a method with different syntax; excluding them would make the ceiling
                // avoidable by extraction rather than by fixing.
                foreach (var local in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
                    if (local.ParameterList.Parameters.Count > ParameterCeiling)
                    {
                        found.Add($"{file}::{local.Identifier.ValueText} = {local.ParameterList.Parameters.Count}");
                    }
            }

            found.Sort(StringComparer.Ordinal);
            return found;
        }

        [Fact]
        public void No_generator_method_exceeds_the_parameter_ceiling()
        {
            var offenders = FindMethodsOverTheCeiling()
                .Where(o => !ParameterAllowance.Contains(o))
                .ToList();

            Assert.True(offenders.Count == 0,
                $"A generator method declares more than {ParameterCeiling} parameters, or an allowed one GREW. "
                + "A call of this width is not checked by anything a reader can see - a transposed pair of "
                + "same-typed arguments compiles clean and no test tells the orders apart. Thread the state "
                + "through ExtractionContext (R26-02) instead of adding a parameter:\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void The_parameter_allowance_is_shrink_only_and_carries_no_stale_rows()
        {
            // The ratchet itself. A row survives only while its method still declares exactly that many
            // parameters; drop one and the row must be rewritten DOWN or deleted. Without this the table would
            // keep describing a debt that has already been paid, and the next reader would believe the migration
            // had done less than it did - the same reasoning as REG-02's allowlist above.
            var actual = FindMethodsOverTheCeiling().ToHashSet(StringComparer.Ordinal);
            var stale = ParameterAllowance.Where(a => !actual.Contains(a)).OrderBy(a => a, StringComparer.Ordinal).ToList();

            Assert.True(stale.Count == 0,
                "Allowance rows no longer match any declaration - the method shrank, was renamed, or is gone. "
                + "Rewrite the row at its new count, or DELETE it if the method is now within the ceiling; the "
                + "list must shrink, never drift:\n  " + string.Join("\n  ", stale));
        }

        private static List<string> FindOptionalOptionParameters()
        {
            var found = new List<string>();

            foreach (var path in RepoPaths.SourceFiles(RepoPaths.GeneratorSrcDir))
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
                var file = Path.GetFileName(path);

                foreach (var method in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
                {
                    var name = method switch
                    {
                        MethodDeclarationSyntax m => m.Identifier.ValueText,
                        ConstructorDeclarationSyntax c => c.Identifier.ValueText,
                        _ => null
                    };

                    if (name is null) { continue; }

                    foreach (var p in method.ParameterList.Parameters)
                    {
                        // A default clause is the whole hazard: without one, omitting the argument is a
                        // compile error, which is exactly the outcome we want.
                        if (p.Default is null) { continue; }

                        if (OptionVocabulary.Contains(p.Identifier.ValueText))
                        {
                            found.Add($"{file}::{name}::{p.Identifier.ValueText}");
                        }
                    }
                }
            }

            found.Sort(StringComparer.Ordinal);
            return found;
        }

        [Fact]
        public void No_generator_method_declares_an_optional_option_parameter()
        {
            var offenders = FindOptionalOptionParameters()
                .Where(o => !Allowlist.Contains(o))
                .ToList();

            Assert.True(offenders.Count == 0,
                "An option parameter is OPTIONAL. ISSUE-043 and ISSUE-044 were both 'the default is the "
                + "permissive value and one call site forgot it' — a bug that produces no error, just the wrong "
                + "behaviour. Make it required, or thread it through ExtractionContext (round 27):\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void The_allowlist_is_shrink_only_and_carries_no_stale_rows()
        {
            // Without this, a row that has already been fixed would sit in the allowlist forever, and the next
            // reader would believe the debt is larger than it is — the allowlist would stop describing reality
            // while still passing. Same reasoning as the ratchet guards elsewhere in this suite.
            var actual = FindOptionalOptionParameters().ToHashSet(StringComparer.Ordinal);
            var stale = Allowlist.Where(a => !actual.Contains(a)).OrderBy(a => a, StringComparer.Ordinal).ToList();

            Assert.True(stale.Count == 0,
                "Allowlist rows no longer match any declaration — they were fixed. DELETE them; the list must "
                + "shrink, never drift:\n  " + string.Join("\n  ", stale));
        }
    }
}
