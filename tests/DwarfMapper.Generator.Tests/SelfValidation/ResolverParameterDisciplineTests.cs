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
            // 15 rows. Was 31 when this scan was written. ResolveMembers lost nine and ResolveProjectionMembers seven when
            // R27-02 bundled their mapper-wide flags into MapperOptions — the allowlist doing exactly what a
            // shrink-only list is for. What remains is the converters and the constructor-argument resolver.
            "CollectionConverter.cs::Synthesize::isPreserve",
            "CollectionConverter.cs::TryResolve::nullAsNull",
            "DictionaryConverter.cs::Synthesize::isPreserve",
            "DictionaryConverter.cs::Synthesize::nullAsNull",
            "MapperExtractor.Conversions.cs::TryResolveConversion::autoNest",
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
