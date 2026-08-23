// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>B31 / DWARF098.</b> <c>[DwarfMapperConstructor]</c> on a constructor the selector cannot use was
    ///     ignored at every endpoint that reads it, without a word. The EMISSION is not the defect and is
    ///     unchanged: <c>ConstructorSelector.IsUsableCandidate</c> filters the annotated constructor and selection
    ///     falls back to the safe default policy, which is right — selecting it would emit code the compiler
    ///     rejects. What was missing is the REPORT. A caller who marks a <c>private</c> constructor and gets
    ///     object-initializer mapping had no way to learn why; measured at A14, the output was byte-identical to
    ///     the unannotated baseline.
    ///     <para>
    ///         <b>A Warning, and that answers the row's two-messages question.</b> The behaviour is defensible and
    ///         is kept, so this reports rather than refuses. An ABSENT annotation stays silent: nothing was
    ///         written, so nothing was discarded, and only the written-and-declined case is a caller mistake.
    ///     </para>
    ///     <para>
    ///         <b>The reason is per-filter, and the remedy is per-reason.</b> A message that named the wrong
    ///         remedy would be worse than silence, and this fix nearly shipped one: the first draft told every
    ///         inaccessible constructor to set <c>[DwarfMapper(AllowNonPublic = true)]</c>, and the probe showed a
    ///         <c>private</c> constructor is STILL filtered with that option set — the option widens the filter
    ///         only as far as the consumer's own assembly can see, which a private member of another type never
    ///         is. Both accessibility cases are pinned below, in both directions.
    ///     </para>
    /// </summary>
    public class AnnotatedConstructorUnusableTests
    {
        private static string Source(
            string ctor,
            string options = "",
            string endpoint = "public partial Dst Map(Src s);")
        {
            return $$"""
                     using System.Linq;
                     using DwarfMapper;
                     namespace Demo;
                     public class Src { public int A { get; set; } }
                     public class Dst
                     {
                         public Dst() { }
                         {{ctor}}
                         public int A { get; set; }
                     }
                     [DwarfMapper({{options}})]
                     public partial class M { {{endpoint}} }
                     """;
        }

        private static string Message(IEnumerable<Diagnostic> diags)
        {
            return string.Join(" | ",
                diags.Where(d => d.Id == "DWARF098")
                    .Select(d => d.GetMessage(CultureInfo.InvariantCulture)));
        }

        /// <summary>Every filter <c>IsUsableCandidate</c> applies, with the words its message must carry.</summary>
        public static TheoryData<string, string, string> UnusableCtors()
        {
            return new TheoryData<string, string, string>
            {
                {
                    "private", "[DwarfMapperConstructor] private Dst(int a) { A = a; }", "it is private"
                },
                {
                    "obsolete", "[DwarfMapperConstructor] [System.Obsolete] public Dst(int a) { A = a; }", "marked [Obsolete]"
                },
                {
                    "ref parameter", "[DwarfMapperConstructor] public Dst(ref int a) { A = a; }", "passed by ref"
                },
                {
                    "out parameter", "[DwarfMapperConstructor] public Dst(out int a) { a = 1; A = 1; }", "passed by out"
                },
                {
                    "copy constructor", "[DwarfMapperConstructor] public Dst(Dst other) { A = other.A; }", "COPY constructor"
                }
            };
        }

        [Theory]
        [MemberData(nameof(UnusableCtors))]
        public void An_unusable_annotated_constructor_is_reported_with_the_reason_that_rejected_it(
            string label,
            string ctor,
            string expectedReason)
        {
            ArgumentNullException.ThrowIfNull(ctor);
            var source = Source(ctor);
            var (diags, generated) = GeneratorTestHarness.Run(source);

            Assert.True(diags.Count(d => d.Id == "DWARF098") == 1,
                $"[{label}] expected exactly one DWARF098; got " + $"{diags.Count(d => d.Id == "DWARF098")}. B31: this was silent at every endpoint.");
            Assert.Contains(expectedReason, Message(diags), StringComparison.Ordinal);
            Assert.Equal(DiagnosticSeverity.Warning, diags.First(d => d.Id == "DWARF098").Severity);

            // The EMISSION is unchanged and must stay so — the fallback is the safe answer, and the fix is a
            // report, not a behaviour change. Byte-identical to the same type with no annotation at all.
            var unannotated = GeneratorTestHarness.Run(Source(ctor.Replace("[DwarfMapperConstructor] ",
                "",
                StringComparison.Ordinal))).GeneratedSource;
            Assert.Equal(unannotated, generated);
            GeneratorAssert.EmitsCompilableCode(source);
        }

        /// <summary>
        ///     The CONTROLS, which are the half that keeps this from being noise: a usable annotated constructor
        ///     is used and says nothing, and no annotation at all says nothing either. Only a directive that was
        ///     WRITTEN can be discarded.
        /// </summary>
        [Theory]
        [InlineData("[DwarfMapperConstructor] public Dst(int a) { A = a; }", "a usable annotated constructor")]
        [InlineData("public Dst(int a) { A = a; }", "no annotation at all")]
        public void Nothing_is_reported_when_no_directive_was_discarded(string ctor, string label)
        {
            var (diags, _) = GeneratorTestHarness.Run(Source(ctor));
            Assert.True(diags.All(d => d.Id != "DWARF098"),
                $"DWARF098 fired for {label}: {Message(diags)}");
        }

        /// <summary>
        ///     The absent-annotation control, once per unusable SHAPE. The control above pairs "no annotation
        ///     at all" with a constructor that is perfectly USABLE, so the report is skipped by the usability
        ///     test and the annotation test never has to answer - the two filters are indistinguishable on that
        ///     input. Every shape the theory above reports on is asked again here with the directive removed:
        ///     the constructor is still unusable, and the rule stated at the top of this file is that only a
        ///     directive that was WRITTEN can be discarded. A selector that reported here would tell a caller
        ///     their own private / obsolete / copy / by-ref constructor was "ignored" when they never asked for
        ///     it - noise on ordinary types, and it would fire on the majority of real destinations.
        /// </summary>
        [Theory]
        [MemberData(nameof(UnusableCtors))]
        public void An_unusable_constructor_carrying_no_directive_is_never_reported(
            string label,
            string ctor,
            string reason)
        {
            ArgumentNullException.ThrowIfNull(ctor);
            var source = Source(ctor.Replace("[DwarfMapperConstructor] ", "", StringComparison.Ordinal));
            var (diags, _) = GeneratorTestHarness.Run(source);
            var reported = Message(diags);

            Assert.True(diags.All(d => d.Id != "DWARF098"),
                $"[{label}] DWARF098 fired for a constructor that carries no directive: {reported}");
            Assert.DoesNotContain(reason, reported, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The two accessibility remedies, and the reason they cannot be one sentence.
        ///     <c>AllowNonPublic</c> widens the candidate filter to what the CONSUMER'S ASSEMBLY can reach, so it
        ///     rescues an <c>internal</c> constructor and can never rescue a <c>private</c> one. Both directions
        ///     are measured here, because "set the option" is advice that sends the private case in a circle.
        /// </summary>
        [Fact]
        public void The_accessibility_remedy_matches_what_the_option_can_actually_reach()
        {
            const string privateCtor = "[DwarfMapperConstructor] private Dst(int a) { A = a; }";
            const string internalCtor = "[DwarfMapperConstructor] internal Dst(int a) { A = a; }";

            // internal, option OFF → reported, and setting the option IS the remedy.
            var (internalOff, _) = GeneratorTestHarness.Run(Source(internalCtor));
            Assert.Contains("AllowNonPublic = true)]. Set that option",
                diagFor(internalOff),
                StringComparison.Ordinal);

            // internal, option ON → the constructor is USED, and there is nothing to report.
            var (internalOn, internalOnGen) = GeneratorTestHarness.Run(Source(internalCtor, "AllowNonPublic = true"));
            Assert.DoesNotContain(internalOn, d => d.Id == "DWARF098");
            Assert.Contains("new global::Demo.Dst(", internalOnGen, StringComparison.Ordinal);

            // private, option ON → STILL filtered, so the message must NOT tell the caller to set the option.
            var (privateOn, _) = GeneratorTestHarness.Run(Source(privateCtor, "AllowNonPublic = true"));
            var msg = diagFor(privateOn);
            Assert.Contains("no mapper option can reach", msg, StringComparison.Ordinal);
            Assert.Contains("Make the constructor internal", msg, StringComparison.Ordinal);

            string diagFor(IEnumerable<Diagnostic> d)
            {
                return Message(d);
            }
        }

        /// <summary>
        ///     Reported at EVERY endpoint that reads the directive, because the silence was at every endpoint.
        ///     A14 measured it at <c>CreateMap</c> and <c>Projection</c>; both are asked here, separately and
        ///     together, so a report wired into one selection path and not the other cannot pass.
        /// </summary>
        [Theory]
        [InlineData("public partial Dst Map(Src s);", 1)]
        [InlineData("public partial IQueryable<Dst> Project(IQueryable<Src> q);", 1)]
        [InlineData("public partial Dst Map(Src s); public partial IQueryable<Dst> Project(IQueryable<Src> q);", 2)]
        public void It_is_reported_once_per_mapping_method_that_selects_the_destination(
            string endpoint,
            int expected)
        {
            var (diags, _) = GeneratorTestHarness.Run(
                Source("[DwarfMapperConstructor] private Dst(int a) { A = a; }", endpoint: endpoint));

            Assert.Equal(expected, diags.Count(d => d.Id == "DWARF098"));
        }

        /// <summary>
        ///     The sentence AFTER the reason, which is the half that answers the question the warning provokes:
        ///     "then what did I get instead?". The emission is unchanged on purpose, and a message that named
        ///     the filter and stopped there would read as a defect report about the mapping rather than about
        ///     the directive. Pinned as TEXT, because a message that merely EXISTS is not a message — the id
        ///     alone was already asserted above and would survive the whole explanation being emptied.
        /// </summary>
        [Fact]
        public void The_message_says_the_fallback_construction_is_deliberate_and_safe()
        {
            var (diags, _) = GeneratorTestHarness.Run(
                Source("[DwarfMapperConstructor] private Dst(int a) { A = a; }"));
            var msg = Message(diags);

            Assert.Contains("constructed exactly as it would be with no annotation at all",
                msg,
                StringComparison.Ordinal);
            Assert.Contains("fallback is deliberate", msg, StringComparison.Ordinal);
            Assert.Contains("emit code the compiler rejects", msg, StringComparison.Ordinal);
            Assert.Contains("the mapping is safe; the directive is not", msg, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The accessibility WORD, one row per modifier the language can put on a constructor. The two
        ///     REMEDIES are measured above; what is measured here is that the message names the modifier the
        ///     caller actually wrote. "It is , and this mapper does not set …" is a sentence with the subject
        ///     removed, and every word below is a separate arm of the map that supplies it.
        ///     <para>
        ///         The trailing comma is part of each expectation on purpose: without it "it is private" also
        ///         matches the <c>private protected</c> message and "it is protected" also matches the
        ///         <c>protected internal</c> one, so four of the five rows would stop discriminating.
        ///     </para>
        ///     <para>
        ///         <c>public</c> has no row, and cannot have one: <c>IsAccessible</c> admits a public
        ///         constructor by definition, so the inaccessible branch that renders these words is
        ///         unreachable with it.
        ///     </para>
        /// </summary>
        [Theory]
        [InlineData("private Dst(int a) { A = a; }", "it is private,")]
        [InlineData("private protected Dst(int a) { A = a; }", "it is private protected,")]
        [InlineData("protected Dst(int a) { A = a; }", "it is protected,")]
        [InlineData("internal Dst(int a) { A = a; }", "it is internal,")]
        [InlineData("protected internal Dst(int a) { A = a; }", "it is protected internal,")]
        public void The_inaccessible_reason_names_the_modifier_the_constructor_carries(
            string ctor,
            string expectedWord)
        {
            var source = Source("[DwarfMapperConstructor] " + ctor);
            var (diags, _) = GeneratorTestHarness.Run(source);

            Assert.True(diags.Count(d => d.Id == "DWARF098") == 1,
                $"[{expectedWord}] expected exactly one DWARF098; got " + $"{diags.Count(d => d.Id == "DWARF098")}.");
            Assert.Contains(expectedWord, Message(diags), StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(source);
        }

        /// <summary>
        ///     The REMEDY half of every reason that is not about accessibility. The opening clause of each arm
        ///     — "it is a COPY constructor", "it is marked [Obsolete]", "passed by ref" — is pinned by the table
        ///     at the top of this file; what is pinned here is the sentence telling the caller what to do
        ///     instead, which is the part a reader acts on and the part that can be dropped without any
        ///     id-level assertion noticing.
        /// </summary>
        [Theory]
        [InlineData("copy", "[DwarfMapperConstructor] public Dst(Dst other) { A = other.A; }", "cannot build the destination from the source", "come from the source type")]
        [InlineData("obsolete", "[DwarfMapperConstructor] [System.Obsolete] public Dst(int a) { A = a; }", "does not generate calls to obsolete members", "Drop the [Obsolete], or annotate a supported constructor")]
        [InlineData("ref", "[DwarfMapperConstructor] public Dst(ref int a) { A = a; }", "which cannot be written as a named argument (CS1620)", "Take it by value or by 'in'")]
        [InlineData("out", "[DwarfMapperConstructor] public Dst(out int a) { a = 1; A = 1; }", "which cannot be written as a named argument (CS1620)", "Take it by value or by 'in'")]
        public void Each_reason_carries_the_remedy_for_the_filter_that_produced_it(
            string label,
            string ctor,
            string diagnosis,
            string remedy)
        {
            var diags = GeneratorTestHarness.Run(Source(ctor)).Diagnostics;
            var msg = Message(diags);

            Assert.True(diags.Count(d => d.Id == "DWARF098") == 1,
                $"[{label}] expected exactly one DWARF098; got " + $"{diags.Count(d => d.Id == "DWARF098")}.");
            Assert.Contains(diagnosis, msg, StringComparison.Ordinal);
            Assert.Contains(remedy, msg, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The message quotes the discarded directive's constructor back by SIGNATURE, and a signature is
        ///     the separated parameter list or it is nothing: a caller with three overloads reads that quoted
        ///     text to learn which of them was declined, and <c>Dst(intstring)</c> names none of them.
        /// </summary>
        [Fact]
        public void The_quoted_signature_separates_the_parameter_types()
        {
            var source = Source("[DwarfMapperConstructor] private Dst(int a, string b) { A = a; }");
            var (diags, _) = GeneratorTestHarness.Run(source);

            Assert.Contains("'Dst(int, string)' is ignored", Message(diags), StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(source);
        }
    }
}
