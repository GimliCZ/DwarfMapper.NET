// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     ISSUE-044. <c>[DwarfMapper(AllowNonPublic = true)]</c> is an opt-in about member VISIBILITY, so it has to
    ///     reach every place that asks "which members can I read from / write to this type?". The shared answer lives
    ///     in <c>MemberFacts.Readable/Writable</c>, and <c>MapperExtractor</c> wraps it as
    ///     <c>ReadableMembers/WritableMembers</c> — wrappers that used to default <c>compilation</c> to <c>null</c>
    ///     and <c>allowNonPublic</c> to <c>false</c>. Twenty call sites took those defaults, so those paths saw only
    ///     PUBLIC members no matter what the mapper opted into.
    ///     <para>
    ///         That is the same regression class <see cref="ProjectionRuntimeParityTests" /> exists for: an option
    ///         threaded into one resolver and silently dropped by another, so one mapper means two different things
    ///         depending on which path reaches the member lookup. The failure is not a crash — it is a member that
    ///         quietly stops resolving, surfacing as DWARF043 ("no member 'X'") or as a member that never gets
    ///         mapped at all.
    ///     </para>
    ///     <para>
    ///         These tests pin the paths an omitting site actually sits on. They are written against BEHAVIOUR
    ///         (does the member resolve?) rather than against the wrapper signature, so they keep their meaning if
    ///         the threading is ever refactored.
    ///     </para>
    /// </summary>
    public class AllowNonPublicMemberLookupTests
    {
        // ── Flatten: dotted [MapProperty] source path (TryResolveSourcePath) ──────────────────────────────
        // The hop `Nested` is internal. With AllowNonPublic the mapper may read it, so the path must resolve.
        [Fact]
        public void Dotted_source_path_resolves_through_an_internal_hop()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Inner { public string Name { get; set; } = ""; }
                               public class S { internal Inner Nested { get; set; } = new(); }
                               public class D { public string NestedName { get; set; } = ""; }
                               [DwarfMapper(AllowNonPublic = true)] public partial class M
                               {
                                   [MapProperty("Nested.Name", nameof(D.NestedName))]
                                   public partial D Map(S s);
                               }
                               """;

            var (diags, gen) = GeneratorTestHarness.Run(src);

            Assert.DoesNotContain(diags, d => d.Id == "DWARF043");
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("s.Nested.Name", gen, StringComparison.Ordinal);
        }

        // Same shape WITHOUT the opt-in: the hop stays invisible and DWARF043 is the correct answer. This is the
        // control — without it, the test above could pass because non-public members are visible to everyone,
        // which would mean the opt-in does nothing.
        [Fact]
        public void Dotted_source_path_does_NOT_resolve_through_an_internal_hop_without_the_flag()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Inner { public string Name { get; set; } = ""; }
                               public class S { internal Inner Nested { get; set; } = new(); }
                               public class D { public string NestedName { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Nested.Name", nameof(D.NestedName))]
                                   public partial D Map(S s);
                               }
                               """;

            var (diags, _) = GeneratorTestHarness.Run(src);

            Assert.Contains(diags, d => d.Id == "DWARF043");
        }

        // ── Flatten: [Flatten("Root")] over an internal root ──────────────────────────────────────────────
        // Flattening is opt-in, not implicit: the root is named, then its leaves are enumerated. Both lookups
        // are separate call sites (find the root; list its leaves), so this covers the pair.
        [Fact]
        public void Flatten_root_resolves_when_the_root_member_is_internal()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Address { public string City { get; set; } = ""; }
                               public class S { internal Address Address { get; set; } = new(); }
                               public class D { public string City { get; set; } = ""; }
                               [DwarfMapper(AllowNonPublic = true)] public partial class M
                               {
                                   [Flatten("Address")]
                                   public partial D Map(S s);
                               }
                               """;

            var (diags, gen) = GeneratorTestHarness.Run(src);

            Assert.DoesNotContain(diags, d => d.Id == "DWARF016"); // "invalid flatten root"
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("s.Address.City", gen, StringComparison.Ordinal);
        }

        // The leaf side of the same question: the root is public, but the leaf's getter is internal.
        [Fact]
        public void Flatten_leaf_resolves_when_the_leaf_getter_is_internal()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Address { public string City { get; internal set; } = ""; }
                               public class S { public Address Address { get; set; } = new(); }
                               public class D { public string City { get; set; } = ""; }
                               [DwarfMapper(AllowNonPublic = true)] public partial class M
                               {
                                   [Flatten("Address")]
                                   public partial D Map(S s);
                               }
                               """;

            var (diags, gen) = GeneratorTestHarness.Run(src);

            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("s.Address.City", gen, StringComparison.Ordinal);
        }

        // ── Flatten: unflatten TARGET path (WritableMembers on the intermediate) ──────────────────────────
        // Mirror of the above on the writable side: the leaf setter on the intermediate is internal.
        [Fact]
        public void Unflatten_target_writes_an_internal_leaf_setter()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Addr { public string City { get; internal set; } = ""; }
                               public class S { public string Town { get; set; } = ""; }
                               public class D { public Addr Address { get; set; } = new(); }
                               [DwarfMapper(AllowNonPublic = true)] public partial class M
                               {
                                   [MapProperty(nameof(S.Town), "Address.City")]
                                   public partial D Map(S s);
                               }
                               """;

            var (diags, gen) = GeneratorTestHarness.Run(src);

            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("City", gen, StringComparison.Ordinal);
        }

        // ── Constructor selection ────────────────────────────────────────────────────────────────────────
        // Selecting between overloads narrows to constructors whose every parameter HAS a source, then picks the
        // widest. That satisfiability scan enumerates the source's readable members, so with the flag dropped an
        // internal source member made the wider constructor look unmappable and the narrower one won.
        [Fact]
        public void Constructor_selection_counts_an_internal_source_member_as_a_source()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public sealed class S { public int Id { get; set; } internal string Secret { get; set; } = ""; }
                               public sealed class D
                               {
                                   public D(int id) { Id = id; Secret = ""; }
                                   public D(int id, string secret) { Id = id; Secret = secret; }
                                   public int Id { get; }
                                   public string Secret { get; }
                               }
                               [DwarfMapper(AllowNonPublic = true)] public partial class M
                               {
                                   public partial D Map(S s);
                               }
                               """;

            var (diags, gen) = GeneratorTestHarness.Run(src);

            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            // The two-parameter constructor is the widest satisfiable one once Secret is visible.
            Assert.Contains("secret:", gen, StringComparison.Ordinal);
        }

        // ── Projection is the deliberate EXCEPTION, and that is the point ────────────────────────────────
        // Threading the flag everywhere is wrong. A projection becomes an expression tree that a query provider
        // translates, and it cannot read a non-public member — so projection refuses AllowNonPublic and says why
        // (DWARF028) instead of resolving the member and producing silently wrong data.
        //
        // This test exists because the ISSUE-044 fix initially DID thread the flag into the projection resolver.
        // It compiled, and it broke this contract; OptionContractTests and ProjectionRuntimeParityTests caught it.
        // Pinning the exception next to the rule is what stops the "fix" being reapplied by someone tidying up an
        // inconsistency they think they see.
        [Fact]
        public void Projection_refuses_AllowNonPublic_and_names_the_real_reason()
        {
            const string src = """
                               using System.Linq;
                               using DwarfMapper;
                               namespace Demo;
                               public sealed class Src { public int Id { get; set; } internal string Secret { get; set; } = ""; }
                               public sealed class Dto { public int Id { get; set; } public string Secret { get; set; } = ""; }
                               [DwarfMapper(AllowNonPublic = true)]
                               public partial class M
                               {
                                   public partial IQueryable<Dto> Project(IQueryable<Src> q);
                               }
                               """;

            var (diags, _) = GeneratorTestHarness.Run(src);

            // DWARF028 (not translatable), NOT DWARF001 (no matching source member) — the member is plainly
            // there, and reporting it as missing would send the reader hunting for something that exists.
            Assert.Contains(diags, d => d.Id == "DWARF028");
            Assert.DoesNotContain(diags, d => d.Id == "DWARF001");
        }

        // The runtime half of the same mapper: what projection refuses, .Map honours. Both behaviours are
        // correct; they differ because the execution models differ, not because an option leaked.
        [Fact]
        public void Runtime_map_honours_AllowNonPublic_where_projection_refuses_it()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public sealed class Src { public int Id { get; set; } internal string Secret { get; set; } = ""; }
                               public sealed class Dto { public int Id { get; set; } public string Secret { get; set; } = ""; }
                               [DwarfMapper(AllowNonPublic = true)]
                               public partial class M
                               {
                                   public partial Dto Map(Src s);
                               }
                               """;

            var (diags, gen) = GeneratorTestHarness.Run(src);

            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("Secret = s.Secret", gen, StringComparison.Ordinal);
        }
    }
}
