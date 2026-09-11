// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>[MapNullSkip]</c> — pair- and method-scoped <c>SkipNullSourceMembers</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         AutoMapper's <c>ForAllMembers(o =&gt; o.Condition((_,_,src) =&gt; src != null))</c> was configured
    ///         <b>per map</b>, while <c>[DwarfMapper(SkipNullSourceMembers = true)]</c> is a whole-class policy.
    ///         A profile mixing patch-merge maps with ordinary ones therefore could not be translated one-for-one
    ///         — the Round-18 migration had to invent two extra mapper classes purely to carry one boolean.
    ///     </para>
    ///     <para>
    ///         That split was not merely inconvenient. A nested pair reached from both classes was synthesized
    ///         twice, once guarded and once not, silently — recorded at the time as "a real behavioural
    ///         difference, not a cosmetic one". Narrowing the scope removes the split, and with it that fork.
    ///     </para>
    /// </remarks>
    public class MapNullSkipScopeTests
    {
        private const string Types = """
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class Dto { public string? Name { get; set; } public string? Note { get; set; } }
                                     public class Entity { public string Name { get; set; } = ""; public string Note { get; set; } = ""; }
                                     """;

        /// <summary>The guard <c>SkipNullSourceMembers</c> emits, as it appears in generated code.</summary>
        private const string Guard = "is not null";

        [Fact]
        public void Method_level_MapNullSkip_guards_only_the_method_that_carries_it()
        {
            // The shape the migration could not express: one mapper, two maps, opposite null semantics.
            var generated = GeneratorAssert.EmitsCompilableCode(Types +
                                                                """

                                                                [DwarfMapper]
                                                                public partial class M
                                                                {
                                                                    public partial void Replace(Dto src, Entity dst);

                                                                    [MapNullSkip]
                                                                    public partial void Patch(Dto src, Entity dst);
                                                                }
                                                                """);

            var replace = Body(generated, "Replace");
            var patch = Body(generated, "Patch");

            Assert.DoesNotContain(Guard, replace, StringComparison.Ordinal);
            Assert.Contains(Guard, patch, StringComparison.Ordinal);
        }

        [Fact]
        public void Method_level_MapNullSkip_false_carves_a_method_out_of_an_enabled_class()
        {
            // The inverse direction. Without it, a class that mostly patches would need the split all over again
            // for the one map that must replace.
            var generated = GeneratorAssert.EmitsCompilableCode(Types +
                                                                """

                                                                [DwarfMapper(SkipNullSourceMembers = true)]
                                                                public partial class M
                                                                {
                                                                    public partial void Patch(Dto src, Entity dst);

                                                                    [MapNullSkip(false)]
                                                                    public partial void Replace(Dto src, Entity dst);
                                                                }
                                                                """);

            Assert.Contains(Guard, Body(generated, "Patch"), StringComparison.Ordinal);
            Assert.DoesNotContain(Guard, Body(generated, "Replace"), StringComparison.Ordinal);
        }

        [Fact]
        public void Pair_scoped_MapNullSkip_applies_to_the_named_GenerateMap_pair()
        {
            // The attribute-only mapper shape, which is what an AutoMapper Profile actually becomes.
            var generated = GeneratorAssert.EmitsCompilableCode(Types +
                                                                """

                                                                [DwarfMapper]
                                                                [GenerateMap<Dto, Entity>]
                                                                [MapNullSkip<Dto, Entity>]
                                                                public partial class M { }
                                                                """);

            Assert.Contains(Guard, generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Pair_scoped_MapNullSkip_does_not_leak_to_a_pair_it_does_not_name()
        {
            // Same rule the Use= fix established: a pair-scoped attribute configures the pair it names and no
            // other. A leak here would silently make an ordinary map stop clearing values.
            //
            // Two SOURCES sharing one target, rather than one source with two targets — the latter is DWARF060
            // (C# cannot overload by return type) and would emit nothing at all, so it cannot express this
            // question.
            var generated = GeneratorAssert.EmitsCompilableCode("""
                                                                using DwarfMapper;
                                                                namespace Demo;
                                                                public class DtoA { public string? Name { get; set; } }
                                                                public class DtoB { public string? Name { get; set; } }
                                                                public class Entity { public string Name { get; set; } = ""; }

                                                                [DwarfMapper]
                                                                [GenerateMap<DtoA, Entity>]
                                                                [GenerateMap<DtoB, Entity>]
                                                                [MapNullSkip<DtoA, Entity>]
                                                                public partial class M { }
                                                                """);

            // Exactly one guarded assignment: the EntityA pair. EntityB must assign unconditionally.
            var guards = generated.Split(Guard).Length - 1;
            Assert.Equal(1, guards);
        }

        [Fact]
        public void Pair_scoped_false_overrides_an_enabled_class()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Types +
                                                                """

                                                                [DwarfMapper(SkipNullSourceMembers = true)]
                                                                [GenerateMap<Dto, Entity>]
                                                                [MapNullSkip<Dto, Entity>(false)]
                                                                public partial class M { }
                                                                """);

            Assert.DoesNotContain(Guard, generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_pair_with_no_attribute_still_inherits_the_class_policy()
        {
            // The narrowing must not change what already worked: the class-level option remains the way to say
            // "every map on this mapper patches", which is easier to read than repeating an attribute per pair.
            var generated = GeneratorAssert.EmitsCompilableCode(Types +
                                                                """

                                                                [DwarfMapper(SkipNullSourceMembers = true)]
                                                                [GenerateMap<Dto, Entity>]
                                                                public partial class M { }
                                                                """);

            Assert.Contains(Guard, generated, StringComparison.Ordinal);
        }

        // ── Which members the guard reaches, and which it must leave alone ────────────────────────────────
        // ApplySkipNullSourceMembers marks a member deferrable only when its destination can be assigned after
        // construction and its source can actually be null. The three tests below pin one side of that
        // boundary each; every one was a hole the mutation leg found (the whole field arm and most of the
        // skip conditions were removable without a failure).

        [Fact]
        public void A_public_mutable_field_target_is_guarded_like_a_settable_property()
        {
            // Fields are destinations too, and a plain mutable field is exactly as post-construction-settable as
            // a { get; set; } property. The field arm has its own readonly/const/required checks — none of
            // which a mutable field trips — so it must land in the deferrable set and get the guard.
            var generated = GeneratorAssert.EmitsCompilableCode("""
                                                                using DwarfMapper;
                                                                namespace Demo;
                                                                public class Dto { public string? Name { get; set; } public string? Note { get; set; } }
                                                                public class Entity { public string Name = ""; public string Note { get; set; } = ""; }

                                                                [DwarfMapper(SkipNullSourceMembers = true)]
                                                                public partial class M { public partial Entity Map(Dto src); }
                                                                """);

            Assert.Contains("if (src.Name is not null) __dwarf_target.Name = src.Name;", generated, StringComparison.Ordinal);
            Assert.Contains("if (src.Note is not null) __dwarf_target.Note = src.Note;", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_When_guarded_member_is_left_to_its_predicate_and_keeps_its_DWARF070()
        {
            // When= and SkipIfSourceNull are mutually exclusive on a member (MemberMap says so), and the emitter
            // writes the predicate guard alone. So the pass must SKIP a When member outright: marking it would
            // not change the guard, but it would clear NullRefIntoNonNullable — and with it the `!` and the
            // DWARF070 that tell the caller a null can still reach `Note` when the predicate is true.
            var (diagnostics, generated) = GeneratorTestHarness.Run("""
                                                                    #nullable enable
                                                                    using DwarfMapper;
                                                                    namespace Demo;
                                                                    public class Dto { public string? Name { get; set; } public string? Note { get; set; } }
                                                                    public class Entity { public string Name { get; set; } = ""; public string Note { get; set; } = ""; }

                                                                    [DwarfMapper(SkipNullSourceMembers = true)]
                                                                    public partial class M
                                                                    {
                                                                        [MapProperty(nameof(Dto.Note), nameof(Entity.Note), When = nameof(HasNote))]
                                                                        public partial Entity Map(Dto src);
                                                                        private static bool HasNote(Dto d) => d.Note is not null;
                                                                    }
                                                                    """,
                NullableContextOptions.Enable);

            Assert.Contains("if (HasNote(src)) __dwarf_target.Note = src.Note!;", generated, StringComparison.Ordinal);
            Assert.Contains("if (src.Name is not null) __dwarf_target.Name = src.Name;", generated, StringComparison.Ordinal);
            var d070 = Assert.Single(diagnostics.Where(d => d.Id == "DWARF070"));
            Assert.Contains("'Note'", d070.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        [Fact]
        public void An_init_only_target_is_not_deferred_and_keeps_its_DWARF070()
        {
            // An init-only member can only be set in the initializer, so it cannot take the post-construction
            // guard: deferring it emits `__dwarf_target.Name = …` after the `new`, which is CS8852 in the
            // consumer's .g.cs. It stays a plain (forgiven, DWARF070-reported) initializer entry while the
            // settable sibling is guarded.
            const string src = """
                               #nullable enable
                               using DwarfMapper;
                               namespace Demo;
                               public class Dto { public string? Name { get; set; } public string? Note { get; set; } }
                               public class Entity { public string Name { get; init; } = ""; public string Note { get; set; } = ""; }

                               [DwarfMapper(SkipNullSourceMembers = true)]
                               public partial class M { public partial Entity Map(Dto src); }
                               """;
            var (diagnostics, generated) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);

            Assert.Contains("Name = src.Name!,", generated, StringComparison.Ordinal);
            Assert.Contains("if (src.Note is not null) __dwarf_target.Note = src.Note;", generated, StringComparison.Ordinal);
            var d070 = Assert.Single(diagnostics.Where(d => d.Id == "DWARF070"));
            Assert.Contains("'Name'", d070.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src, NullableContextOptions.Enable);
        }

        [Fact]
        public void Pair_scoped_MapNullSkip_reaches_a_pair_declared_as_a_partial_method()
        {
            // D7. The pair-scoped form was read only by the [GenerateMap] and auto-synthesized paths, so a mapper
            // that declares its pair as a METHOD got nothing from it — silently, on the endpoint patch-merge is
            // chiefly for. "Pair-scoped attributes do not reach method-declared pairs" was never the rule:
            // [MapProperty<S,T>] and its siblings always did.
            var generated = GeneratorAssert.EmitsCompilableCode(Types +
                                                                """

                                                                [DwarfMapper]
                                                                [MapNullSkip<Dto, Entity>]
                                                                public partial class M
                                                                {
                                                                    public partial Entity Create(Dto src);

                                                                    public partial void Patch(Dto src, Entity dst);
                                                                }
                                                                """);

            Assert.Contains(Guard, Body(generated, "Create"), StringComparison.Ordinal);
            Assert.Contains(Guard, Body(generated, "Patch"), StringComparison.Ordinal);
        }

        [Fact]
        public void A_method_form_outranks_a_contradicting_pair_form_in_both_directions()
        {
            // Newly reachable: until the two forms fed ONE resolution, no input could set them against each other.
            // Most-specific-wins, which is what both attributes' documentation already implied — the method form
            // exists to "carve one method out of a class", and carving out only works if it outranks the class.
            var pairOnMethodOff = GeneratorAssert.EmitsCompilableCode(Types +
                                                                      """

                                                                      [DwarfMapper]
                                                                      [MapNullSkip<Dto, Entity>(true)]
                                                                      public partial class M
                                                                      {
                                                                          [MapNullSkip(false)]
                                                                          public partial void Replace(Dto src, Entity dst);
                                                                      }
                                                                      """);
            Assert.DoesNotContain(Guard, Body(pairOnMethodOff, "Replace"), StringComparison.Ordinal);

            var pairOffMethodOn = GeneratorAssert.EmitsCompilableCode(Types +
                                                                      """

                                                                      [DwarfMapper]
                                                                      [MapNullSkip<Dto, Entity>(false)]
                                                                      public partial class M
                                                                      {
                                                                          [MapNullSkip(true)]
                                                                          public partial void Patch(Dto src, Entity dst);
                                                                      }
                                                                      """);
            Assert.Contains(Guard, Body(pairOffMethodOn, "Patch"), StringComparison.Ordinal);
        }

        [Fact]
        public void The_method_form_is_refused_element_wise_and_the_pair_form_is_honoured_there()
        {
            // D6. A span or async-stream map takes its configuration only from directives that NAME the pair, so
            // the method form cannot reach it — the same rule DWARF090 already stated for [MapIgnore] and
            // [MapProperty]. Refused rather than propagated, for the reason DWARF077 gives: one method's directive
            // must not silently reconfigure a mapper another route to the pair shares.
            const string spanMethod = """

                                      [DwarfMapper]
                                      public partial class M
                                      {
                                          [MapNullSkip]
                                          public partial void MapSpan(System.ReadOnlySpan<Dto> s, System.Span<Entity> d);
                                      }
                                      """;

            var reported = GeneratorAssert.Reports(Types + spanMethod, "DWARF090");
            Assert.Contains(reported,
                d => d.GetMessage(CultureInfo.InvariantCulture)
                    .Contains("[MapNullSkip<Dto, Entity>(true)]", StringComparison.Ordinal));

            // And the remedy the message prescribes must actually work here, or DWARF090 would be sending the
            // caller to a form that does nothing either.
            var withPairForm = GeneratorAssert.EmitsCompilableCode(Types +
                                                                   """

                                                                   [DwarfMapper]
                                                                   [MapNullSkip<Dto, Entity>]
                                                                   public partial class M
                                                                   {
                                                                       public partial void MapSpan(System.ReadOnlySpan<Dto> s, System.Span<Entity> d);
                                                                   }
                                                                   """);
            Assert.Contains(Guard, withPairForm, StringComparison.Ordinal);
            GeneratorAssert.DoesNotReport(Types +
                                          """

                                          [DwarfMapper]
                                          [MapNullSkip<Dto, Entity>]
                                          public partial class M
                                          {
                                              public partial void MapSpan(System.ReadOnlySpan<Dto> s, System.Span<Entity> d);
                                          }
                                          """,
                "DWARF090");
        }

        [Fact]
        public void The_element_wise_remedy_carries_the_value_that_was_written_not_the_default()
        {
            // The assertion above uses a BARE [MapNullSkip], whose remedy renders (true) — indistinguishable from
            // the constructor default, so it cannot tell a message that echoes the caller's value from one that
            // always prints `true`. This is the case that can: a written `false` must come back as `false`.
            //
            // It is the hazard the arm was built around. [MapNullSkip(false)] on a class that enables skipping means
            // "replace, do not patch"; a remedy of [MapNullSkip<Dto, Entity>] or [MapNullSkip<Dto, Entity>(true)]
            // copied out in answer to it would turn the guard ON — actively harmful advice, worse than the silence
            // DWARF090 replaced, and it would look correct in a diff.
            var reported = GeneratorAssert.Reports(Types +
                                                   """

                                                   [DwarfMapper(SkipNullSourceMembers = true)]
                                                   public partial class M
                                                   {
                                                       [MapNullSkip(false)]
                                                       public partial void MapSpan(System.ReadOnlySpan<Dto> s, System.Span<Entity> d);
                                                   }
                                                   """,
                "DWARF090");

            var message = Assert.Single(
                reported.Select(d => d.GetMessage(CultureInfo.InvariantCulture)),
                m => m.Contains("MapNullSkip", StringComparison.Ordinal));

            Assert.Contains("[MapNullSkip(false)] on this mapping method", message, StringComparison.Ordinal);
            Assert.Contains("[MapNullSkip<Dto, Entity>(false)]", message, StringComparison.Ordinal);
            Assert.DoesNotContain("(true)", message, StringComparison.Ordinal);

            // And the tail must state the endpoint the method form actually reaches. This sentence has been wrong
            // in BOTH directions already: it shipped as "refused at projection" while the projection resolver was
            // not fed the scoped forms and the endpoint was silent, was corrected to "silent at projection", and
            // that correction went stale the moment the threading landed (D6/D7 closed). Projection now refuses,
            // as DWARF028, and the pin runs both ways so neither drift can come back unnoticed.
            Assert.Contains("refused at projection (DWARF028", message, StringComparison.Ordinal);
            Assert.DoesNotContain("silent at projection", message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_malformed_or_absent_argument_falls_back_to_enabled_at_every_scope()
        {
            // The three-state reader treats "present but unreadable" as the constructor's default rather than as
            // absent, at BOTH scopes, because one reader now serves every endpoint: a fallback that differed per
            // scope would be the D6/D7 shape again one level down. The bare form is the reachable case (an
            // argument of the wrong TYPE is CS1503 and never compiles); it is pinned here because the unified
            // resolver reads the value on paths that never saw it before.
            var bareMethod = GeneratorAssert.EmitsCompilableCode(Types +
                                                                 """

                                                                 [DwarfMapper]
                                                                 public partial class M
                                                                 {
                                                                     [MapNullSkip]
                                                                     public partial void Patch(Dto src, Entity dst);
                                                                 }
                                                                 """);
            Assert.Contains(Guard, Body(bareMethod, "Patch"), StringComparison.Ordinal);

            var barePair = GeneratorAssert.EmitsCompilableCode(Types +
                                                               """

                                                               [DwarfMapper]
                                                               [MapNullSkip<Dto, Entity>]
                                                               public partial class M
                                                               {
                                                                   public partial void Patch(Dto src, Entity dst);
                                                               }
                                                               """);
            Assert.Contains(Guard, Body(barePair, "Patch"), StringComparison.Ordinal);
        }

        [Fact]
        public void Both_attribute_forms_are_public_and_carry_an_Enabled_flag()
        {
            // Names the types outright rather than only their [Attribute] shorthand, so the public-surface scan
            // can see them — and pins that the three-state design (present-true / present-false / absent) is
            // expressed by a constructor flag, which is why this is a separate attribute rather than a named
            // property on [GenerateMap] (an attribute argument cannot be bool?).
            Assert.True(typeof(MapNullSkipAttribute).IsPublic);
            Assert.True(typeof(MapNullSkipAttribute<,>).IsPublic);

            Assert.True(new MapNullSkipAttribute().Enabled);
            Assert.False(new MapNullSkipAttribute(false).Enabled);
            Assert.True(new MapNullSkipAttribute<string, string>().Enabled);
            Assert.False(new MapNullSkipAttribute<string, string>(false).Enabled);
        }

        /// <summary>The generated body of one mapping method, so per-method assertions cannot read a sibling.</summary>
        private static string Body(string generated, string methodName)
        {
            var start = generated.IndexOf(" " + methodName + "(", StringComparison.Ordinal);
            Assert.True(start >= 0, $"method '{methodName}' not found in:\n{generated}");

            // Up to the next method declaration, or the end of the file.
            var next = generated.IndexOf("    public ", start + 1, StringComparison.Ordinal);
            return next < 0 ? generated[start..] : generated[start..next];
        }
    }
}
