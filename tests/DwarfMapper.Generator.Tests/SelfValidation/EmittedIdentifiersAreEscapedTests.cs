// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     RULE 1: no emitter may write a name it read off a symbol into generated code raw.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A consumer may write <c>@class</c> — legal C# — and <see cref="Microsoft.CodeAnalysis.ISymbol.Name" />
    ///         hands the generator back <c>class</c> with the <c>@</c> stripped. Written straight out, that is
    ///         <c>class = src.class,</c> in a file the consumer never wrote and cannot fix, with no DwarfMapper
    ///         diagnostic. Thirty-five naming positions were driven through the real generator and twenty-five
    ///         emitted exactly that; <c>ConsumerNamedPositionsCompileTests</c> is the measurement.
    ///     </para>
    ///     <para>
    ///         <b>Why this is a scan and not a review rule.</b> The mechanism is already correct wherever it is
    ///         used — <c>SymbolDisplayFormat.FullyQualifiedFormat</c> and <c>MinimallyQualifiedFormat</c> both
    ///         carry <c>EscapeKeywordIdentifiers</c>, so every type reference has been right all along. The
    ///         defect is never "the escape is wrong", it is "this one site forgot", which is precisely the class
    ///         a scan closes and a reviewer does not: an earlier audit in this project found a fix applied to one
    ///         of N identical construction sites, and <c>b888cc3</c> escaped the EXTRA parameters and not the
    ///         first one, in the same signature.
    ///     </para>
    ///     <para>
    ///         <b>What replaced what.</b> Until 2026-09-07 this file scanned a hand list of TWO emitter files for
    ///         a regex over TWO model fields — roughly 8 % of the population — and the <c>[MapTo]</c> registry
    ///         generator, which has its own emission, was outside it entirely. It was widened rather than
    ///         replaced: its idiom (per-site exemptions carrying reasons, plus a staleness check on the exemption
    ///         list) is the right one and is kept.
    ///     </para>
    /// </remarks>
    public class EmittedIdentifiersAreEscapedTests
    {
        /// <summary>
        ///     THE EMITTING POPULATION, pinned. A file that reaches emitted code must be classified before it can
        ///     ship, on the precedent <c>stryker-config.codefixes.json</c> states for its own list: "adding a
        ///     fifth is a deliberate act that shows up in review".
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         It is the stated UNION of two discriminators, and the union is the finding rather than a
        ///         precaution. "Files that call the source writer" was tested and fails:
        ///         <c>AddNormalizedSource</c>, the single funnel every generated file passes through, is called
        ///         from exactly two files, and NEITHER is <c>MapperExtractor.Phases.cs</c> — where
        ///         <c>b888cc3</c>'s defect lived and where the parameter signature is still built. That file
        ///         touches no writer at all: it puts a C# string in the MODEL, and the emitter appends it
        ///         verbatim. So the second discriminator is model-transitive, and neither alone is the
        ///         population.
        ///     </para>
        ///     <para>
        ///         Both are COMPUTED from the source below and compared against this list, so the list cannot
        ///         quietly go stale and a newly-emitting file cannot quietly join.
        ///     </para>
        /// </remarks>
        private static readonly string[] EmittingFiles =
        [
            "DwarfGenerator.cs",
            Path.Combine("Pipeline", "AggregateEmitter.cs"),
            Path.Combine("Pipeline", "AmbientValidator.cs"),
            Path.Combine("Pipeline", "CollectionConverter.cs"),
            Path.Combine("Pipeline", "DenseEnumProof.cs"),
            Path.Combine("Pipeline", "DictionaryConverter.cs"),
            Path.Combine("Pipeline", "EnumConverter.cs"),
            Path.Combine("Pipeline", "MapEmitter.SpanMap.cs"),
            Path.Combine("Pipeline", "MapEmitter.cs"),
            Path.Combine("Pipeline", "MapperExtractor.Attributes.cs"),
            Path.Combine("Pipeline", "MapperExtractor.Diagnostics.cs"),
            Path.Combine("Pipeline", "MapperExtractor.Flatten.Directive.cs"),
            Path.Combine("Pipeline", "MapperExtractor.Flatten.Hetero.cs"),
            Path.Combine("Pipeline", "MapperExtractor.Flatten.cs"),
            Path.Combine("Pipeline", "MapperExtractor.Hetero.Arms.cs"),
            Path.Combine("Pipeline", "MapperExtractor.Members.Phases.cs"),
            Path.Combine("Pipeline", "MapperExtractor.Members.cs"),
            Path.Combine("Pipeline", "MapperExtractor.Phases.cs"),
            Path.Combine("Pipeline", "MapperExtractor.Projection.cs"),
            Path.Combine("Pipeline", "MapperExtractor.cs"),
            Path.Combine("Pipeline", "NestedMappingRegistry.cs"),
            Path.Combine("Pipeline", "NumericConverter.cs"),
            Path.Combine("Pipeline", "ParsableConverter.cs"),
            Path.Combine("Pipeline", "UserConversionConverter.cs"),
            Path.Combine("Registry", "MapToGenerator.cs")
        ];

        /// <summary>
        ///     Files the discriminators match that are MACHINERY rather than emitters — they define the writing
        ///     apparatus instead of using it, so they have no symbol name to escape.
        /// </summary>
        private static readonly (string File, string Why)[] NotAnEmitter =
        [
            (Path.Combine("Core", "CodeWriter.cs"), "IS the writer. It has no model and no symbols."),
            (Path.Combine("Core", "Identifiers.cs"), "IS the escape. Scanning it would ban the fix."),
            ("GeneratedSourceExtensions.cs",
                "The AddSource funnel: it normalises line endings and hands the text on. It never composes one.")
        ];

        /// <summary>
        ///     Model fields that carry a SYMBOL-DERIVED name and have an <c>Emit*</c> sibling that escapes it.
        ///     Writing the raw one into generated code is the defect this scan exists for.
        /// </summary>
        private static readonly string[] RawNameFields =
        [
            "TargetName", "SourceName", "MethodName", "ParameterName", "ClassName", "ContainingTypes",
            "ConventionMethodNames", "UpdateTargetParameterName", "SpanTargetParameterName",
            "AsyncCancellationParam", "FactoryMethod", "BeforeHooks", "ConverterMethod", "WhenPredicate",
            "UpsertKeyMember", "ForwardName", "BackwardName", "DestMember",

            // Not a model field: ISymbol.Name itself, the accessor every one of the above ultimately came from
            // and the one that is lossy in exactly the way that breaks emission.
            "Name"
        ];

        /// <summary>
        ///     Methods that WRITE emitted text. A raw name reaching one of these as an argument — at any depth,
        ///     including through a concatenation — is an emission.
        /// </summary>
        private static readonly string[] TextWriters = ["Append", "AppendLine", "AppendFormat", "Line", "Block", "Write", "WriteLine"];

        /// <summary>
        ///     Sites where a raw name genuinely is not an identifier in emitted code, each with its reason.
        /// </summary>
        /// <remarks>
        ///     Kept as line-anchored TEXT rather than a line number so an exemption cannot rot into covering
        ///     whatever moves onto that line later, and held to still matching by
        ///     <see cref="The_exemption_list_still_matches_real_code" />.
        /// </remarks>
        private static readonly (string Line, string Why)[] NotEmission =
        [
            ("divergent.Add(member.Name + \" -> \\\"\" + serialized + \"\\\"\");",
                "DWARF083's message text — a list of member names shown to a human, not code."),

            ("enumType.Name + \"' maps to strings that are not its member identifiers: \"",
                "The same diagnostic's prose. The type is NAMED to the reader, not referenced in C#."),

            ("AsSpan(\\\"\" + m.Name + \"\\\")",
                "A STRING LITERAL of the enum member's runtime name, which Enum.ToString produces unescaped. " +
                "The identifier on the same line IS escaped; escaping the literal too would make the flags " +
                "parser fail to recognise its own output."),

            ("targetMemberName + \".\" + tgtMember.Name));",
                "A projection DIAGNOSTIC's path label (DWARF's untranslatable-member message), not an expression."),

            ("targetMemberName + \".\" + tgtMember.Name,",
                "The same label, passed to the same diagnostic on the sibling branch."),

            ("var emitClassName = separateEmit ? classSymbol.Name + \"Mapper\" : classSymbol.Name;",
                "COMPOSES a new type name for the co-located form. A composed name cannot itself be a keyword, " +
                "and MapperClassModel.EmitClassName escapes the non-composed branch at emission. Adding an @ " +
                "here would produce '@classMapper' — an @ mid-identifier is a parse error, not an escape."),

            ("\"To\" + target.Name,",
                "The [MapTo] registry's composed extension-method name — same direction as above. Its source " +
                "is ISymbol.Name, which carries no @ to strip, so it is already correct."),

            ("\"__DwarfRegistry_\" + source.Name,",
                "The registry's composed extension-CLASS name — composed, therefore never a keyword."),

            ("return (TTarget)(object)\" + t.MethodName + \"(source);",
                "TargetPlan.MethodName is the COMPOSED \"To\" + target.Name two hundred lines up, not a name " +
                "any consumer chose, so it cannot be a keyword and has nothing to escape."),

            ("w.Block(\"public static \" + t.TargetFqn + \" \" + t.MethodName",
                "The declaration of the same composed method. Escaping here and not at the call above — or the " +
                "reverse — is exactly the one-of-N-sites hazard, so both are exempted together and for one " +
                "reason.")
        ];

        /// <summary>
        ///     Non-vacuity floors (B9). A scan that silently matches nothing is a failure mode this round has now
        ///     recorded five times, so the instrument states what it must find for its green to mean anything.
        /// </summary>
        /// <summary>
        ///     Measured 2026-09-07: the walk sees 1,565 text-writing calls across the 24 emitting files.
        /// </summary>
        /// <remarks>
        ///     A SANITY FLOOR, deliberately below the measurement and deliberately NOT a ratchet: the count
        ///     moves with every ordinary edit to an emitter, and pinning it exactly would fail this test for
        ///     reasons that have nothing to do with escaping. What it must catch is the walk finding nothing —
        ///     a rename of the writer methods, or an argument traversal that stops traversing. The floor that
        ///     IS exact is <see cref="MinimumEscapedEmissionSites" />, which counts the thing this file is
        ///     about.
        /// </remarks>
        private const int MinimumWriteCallsScanned = 1500;

        /// <summary>
        ///     Measured 2026-09-10 (was 77 as of 2026-09-07): 75 escaped names (an <c>Emit*</c> sibling or an
        ///     <c>Identifiers.*</c> call) reach a writer. Re-pinned by round 30's coverage sweep, which deleted
        ///     <c>MapEmitter.cs</c>'s dead "legacy flat Members path" projection arm — genuinely unreachable
        ///     (<c>method.Members</c> is always empty for a projection method model) and never entered by any
        ///     test — taking its two <c>member.EmitTargetName</c>/<c>member.EmitSourceName</c> call sites with
        ///     it. This is the floor that distinguishes "the scan found no violation" from "the scan found
        ///     nothing at all" — the failure mode this round has now recorded five times.
        /// </summary>
        private const int MinimumEscapedEmissionSites = 75;

        [Fact]
        public void The_emitting_population_is_the_union_of_both_discriminators()
        {
            var root = RepoPaths.GeneratorSrcDir;
            var writesSource = new List<string>();
            var fillsTheModel = new List<string>();

            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(path))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(root, path);
                var code = StripComments(File.ReadAllText(path));

                // Discriminator 1 — the file WRITES C#: it holds a writer, or hands text to AddSource.
                if (code.Contains("CodeWriter", StringComparison.Ordinal) ||
                    code.Contains("StringBuilder", StringComparison.Ordinal) ||
                    code.Contains("AddNormalizedSource", StringComparison.Ordinal) ||
                    code.Contains(".AddSource(", StringComparison.Ordinal))
                {
                    writesSource.Add(relative);
                }

                // Discriminator 2 — the file puts a string into a MODEL field that is later emitted verbatim.
                // This is the half "files that call the writer" misses, and missing it is not hypothetical:
                // b888cc3's defect lived in MapperExtractor.Phases.cs, which touches no writer at all.
                if (ModelConstructions.Any(c => code.Contains(c, StringComparison.Ordinal)))
                {
                    fillsTheModel.Add(relative);
                }
            }

            // Non-vacuity: either discriminator finding nothing would make the union agree with an empty list.
            Assert.True(writesSource.Count > 10, $"Discriminator 1 found only {writesSource.Count} file(s) — it has stopped discriminating.");
            Assert.True(fillsTheModel.Count > 10, $"Discriminator 2 found only {fillsTheModel.Count} file(s) — it has stopped discriminating.");

            var union = writesSource.Concat(fillsTheModel)
                .Distinct(StringComparer.Ordinal)
                .Where(f => !Array.Exists(NotAnEmitter, e => string.Equals(e.File, f, StringComparison.Ordinal)))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            var pinned = EmittingFiles.OrderBy(f => f, StringComparer.Ordinal).ToList();

            var joined = union.Except(pinned, StringComparer.Ordinal).ToList();
            var left = pinned.Except(union, StringComparer.Ordinal).ToList();

            Assert.True(joined.Count == 0,
                "File(s) that now reach emitted code but are not in the pinned emitting population:\n  " +
                string.Join("\n  ", joined) +
                "\n\nAdd them to EmittingFiles (so the escaping scan covers them), or to NotAnEmitter with a " +
                "reason if they are machinery rather than emitters. Either way it is a deliberate act that " +
                "shows up in review.");

            Assert.True(left.Count == 0,
                "Pinned emitting file(s) that no longer match either discriminator — deleted, renamed, or they " +
                "genuinely stopped emitting:\n  " + string.Join("\n  ", left));
        }

        [Fact]
        public void No_emitter_writes_a_raw_symbol_derived_name()
        {
            var offenders = new List<string>();
            var writeCalls = 0;
            var escapedSites = 0;

            foreach (var relative in EmittingFiles)
            {
                var path = Path.Combine(RepoPaths.GeneratorSrcDir, relative);
                Assert.True(File.Exists(path), $"Emitting file not found — has it moved? {relative}");

                var text = File.ReadAllText(path);
                var root = CSharpSyntaxTree.ParseText(text).GetRoot();

                foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (call.Expression is not MemberAccessExpressionSyntax writer ||
                        !TextWriters.Contains(writer.Name.Identifier.ValueText, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    writeCalls++;

                    foreach (var argument in call.ArgumentList.Arguments)
                    foreach (var access in argument.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
                    {
                        var member = access.Name.Identifier.ValueText;
                        if (member.StartsWith("Emit", StringComparison.Ordinal))
                        {
                            escapedSites++;
                            continue;
                        }

                        if (!RawNameFields.Contains(member, StringComparer.Ordinal))
                        {
                            continue;
                        }

                        // Already inside Identifiers.Escape / EscapeTypeName / EscapePath / Unescaped — the
                        // second sanctioned mechanism, for a name read straight off a symbol rather than
                        // carried on a model that could own an Emit* sibling.
                        if (IsWrappedInAnIdentifiersCall(access, call))
                        {
                            escapedSites++;
                            continue;
                        }

                        var line = access.GetLocation().GetLineSpan().StartLinePosition.Line;
                        var sourceLine = text.Split('\n')[line].Trim();

                        if (Array.Exists(NotEmission, e => sourceLine.Contains(e.Line, StringComparison.Ordinal)))
                        {
                            continue;
                        }

                        offenders.Add($"{relative}:{line + 1}  {sourceLine}");
                    }
                }
            }

            // Non-vacuity (B9), twice over: the scan must have found write calls at all, and must have seen the
            // ESCAPED form it is asking for. A syntax walk that matched no invocation, or a rename that made
            // every Emit* property invisible, would otherwise report a clean green over nothing.
            Assert.True(writeCalls >= MinimumWriteCallsScanned,
                $"The scan examined only {writeCalls} text-writing call(s) across {EmittingFiles.Length} files " +
                $"(floor {MinimumWriteCallsScanned}). It has stopped finding the emitters.");
            Assert.True(escapedSites >= MinimumEscapedEmissionSites,
                $"The scan saw only {escapedSites} escaped (Emit*) name(s) reaching a writer (floor " +
                $"{MinimumEscapedEmissionSites}). Either the Emit* convention has been renamed away or the walk " +
                "is not reaching the arguments — in both cases a raw name would now pass unnoticed.");

            Assert.True(offenders.Count == 0,
                "Emitter site(s) writing a RAW symbol-derived name into generated code:\n  " +
                string.Join("\n  ", offenders.Distinct(StringComparer.Ordinal)) +
                "\n\nEmit the model's Emit* sibling instead (EmitTargetName, EmitMethodName, EmitClassName, …), " +
                "or Identifiers.Escape / EscapeTypeName / EscapePath for a name the generator read off a symbol " +
                "itself. A DTO member called @class arrives from ISymbol.Name as 'class', and writing that " +
                "produces 'class = src.class,' — parsed as a malformed event declaration, out of generated " +
                "code, with no DwarfMapper diagnostic.\n\nIf the name is one the generator COMPOSES rather than " +
                "emits whole, the fix is Identifiers.Unescaped and not an escape: an @ in the middle of an " +
                "identifier is a parse error.\n\nIf the use genuinely is not emission (diagnostic prose, a " +
                "string literal), add the line to NotEmission with the reason.");
        }

        [Fact]
        public void The_exemption_list_still_matches_real_code()
        {
            // An exemption that no longer matches anything is a claim about the code that has stopped being
            // true, and it would silently keep covering whatever moves onto that line next.
            var all = string.Join("\n",
                EmittingFiles.Select(f => File.ReadAllText(Path.Combine(RepoPaths.GeneratorSrcDir, f))));

            var stale = NotEmission
                .Where(e => !all.Contains(e.Line, StringComparison.Ordinal))
                .Select(e => e.Line)
                .ToList();

            Assert.True(stale.Count == 0,
                "Exemption(s) that match no line any more — delete them:\n  " + string.Join("\n  ", stale));

            var staleFiles = NotAnEmitter
                .Where(e => !File.Exists(Path.Combine(RepoPaths.GeneratorSrcDir, e.File)))
                .Select(e => e.File)
                .ToList();

            Assert.True(staleFiles.Count == 0,
                "NotAnEmitter names file(s) that no longer exist:\n  " + string.Join("\n  ", staleFiles));

            Assert.All(NotEmission, e => Assert.False(string.IsNullOrWhiteSpace(e.Why)));
            Assert.All(NotAnEmitter, e => Assert.False(string.IsNullOrWhiteSpace(e.Why)));
        }

        /// <summary>
        ///     Model constructions whose string members are appended to emitted code verbatim. Discriminator 2 is
        ///     "the file builds one of these".
        /// </summary>
        private static readonly string[] ModelConstructions =
        [
            "new MemberMap(", "new MapMethodModel(", "new MapperClassModel(", "new SynthesizedMethod(",
            "new HookCall(", "new DerivedTypeArm(", "new RoundTripPair(", "new HandWrittenProvide(",
            "new ProjectionMemberMap(", "new Assignment(", "new FlattenGraphDirective(",
            "ValueExpression:", "SourceAccessExpression:", "ParameterTypeSignature:", "ReturnTypeSignature:",
            "ExtraParameters:"
        ];

        /// <summary>
        ///     Whether <paramref name="access" /> sits inside a call to <c>Identifiers.*</c>, walking outwards no
        ///     further than <paramref name="writeCall" /> — the writer whose arguments are being inspected.
        /// </summary>
        /// <remarks>
        ///     Anchored on the CLASS name rather than on the method, so a new helper on it — this task added
        ///     <c>Unescaped</c> — is covered the day it is written instead of the day someone remembers to widen
        ///     a list of method names.
        /// </remarks>
        private static bool IsWrappedInAnIdentifiersCall(SyntaxNode access, SyntaxNode writeCall)
        {
            for (var node = access.Parent; node is not null && node != writeCall; node = node.Parent)
                if (node is InvocationExpressionSyntax
                    {
                        Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "Identifiers" } }
                    })
                {
                    return true;
                }

            return false;
        }

        private static bool IsBuildOutput(string path)
        {
            return path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                   path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
        }

        /// <summary>
        ///     Comments removed before the discriminators run, so a file that merely MENTIONS <c>CodeWriter</c> in
        ///     a doc comment does not join the emitting population — <c>Model/MapperClassModel.cs</c> did exactly
        ///     that under the previous, textual version of this scan.
        /// </summary>
        private static string StripComments(string source)
        {
            var sb = new System.Text.StringBuilder(source.Length);

            foreach (var token in CSharpSyntaxTree.ParseText(source).GetRoot().DescendantTokens())
            {
                foreach (var trivia in token.LeadingTrivia)
                    if (!IsComment(trivia))
                    {
                        sb.Append(trivia.ToFullString());
                    }

                sb.Append(token.Text);

                foreach (var trivia in token.TrailingTrivia)
                    if (!IsComment(trivia))
                    {
                        sb.Append(trivia.ToFullString());
                    }
            }

            return sb.ToString();
        }

        private static bool IsComment(SyntaxTrivia trivia)
        {
            return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                   trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                   trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                   trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
        }
    }
}
