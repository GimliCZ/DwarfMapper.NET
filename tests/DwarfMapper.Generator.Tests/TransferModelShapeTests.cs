// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>TransferModelShape.Classify</c> — "could this class be a <c>readonly record struct</c>?" (round 29,
    ///     T2.1). The predicate underneath <c>DWARF103</c>; it reports nothing itself.
    ///     <para>
    ///         Most of these tests assert a REFUSAL, and they are the ones that matter. Every refusal below is a
    ///         class whose conversion would change behaviour a consumer relies on — an entity the ORM tracks by
    ///         reference, a type someone derives from, a constructor that validates — and suggesting the rewrite
    ///         there is worse than staying quiet, because the code fix in T2.3 deliberately does not touch
    ///         usages. Each test names the reason string it expects, because that string is what T2.2 puts in
    ///         front of a consumer.
    ///     </para>
    ///     <para>
    ///         <b>Every size below is derived from ALIGNMENT, not from member size</b> — the trap T0.3b recorded:
    ///         a 16-byte <c>Guid</c> is 4-aligned, so it packs after an 8-byte <c>long</c>. The arithmetic is
    ///         <see cref="LayoutHygiene" />'s and is proven against the runtime by its own tests and by
    ///         <c>BclLayoutFactsTests</c>; what these tests pin is that the classifier feeds it the right members
    ///         and reads the thresholds off the answer.
    ///     </para>
    ///     <para>
    ///         A size carrying <c>SizeIsUpperBound</c> is one where a member was counted as an 8-byte reference
    ///         field. That is the x64 width; on x86 it is 4, so the real struct can only be SMALLER than the
    ///         number reported — which is why an <c>Eligible</c> verdict cannot become wrong under it, only
    ///         conservative.
    ///     </para>
    /// </summary>
    public class TransferModelShapeTests
    {
        /// <summary>
        ///     Compiles <paramref name="source" /> and returns every named type declared in it, keyed by name.
        ///     Same shape as <c>LayoutHygieneTests.Compile</c> — a symbol-level unit test needs a compilation
        ///     and nothing else. Unlike that one it ASSERTS the fixture compiled: these sources carry
        ///     attributes, interfaces and a stand-in <c>DbSet&lt;T&gt;</c>, and a fixture with a typo would
        ///     otherwise be classified as some other shape and quietly pass.
        /// </summary>
        private static (Compilation Compilation, IReadOnlyDictionary<string, INamedTypeSymbol> Types)
            Compile(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var refs = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .Append(MetadataReference.CreateFromFile(typeof(DwarfMapperAttribute).Assembly.Location));

            var compilation = CSharpCompilation.Create(
                "TransferModelTestAsm_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    tree
                },
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(tree);
            var errors = model.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, "fixture did not compile: " + string.Join("; ", errors));

            var root = tree.GetRoot();
            var types = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            foreach (var decl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                if (model.GetDeclaredSymbol(decl) is { } named)
                {
                    types[named.Name] = named;
                }

            foreach (var decl in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
                if (model.GetDeclaredSymbol(decl) is { } named)
                {
                    types[named.Name] = named;
                }

            return (compilation, types);
        }

        private static TransferModelShape.Verdict ClassifyType(string source, string typeName = "Dto")
        {
            var (compilation, types) = Compile(source);
            return TransferModelShape.Classify(types[typeName], compilation);
        }

        /// <summary>A class with <paramref name="count" /> one-byte auto-properties: size n, alignment 1.</summary>
        /// <remarks>
        ///     The only way to land on a size that is not a multiple of an alignment — 33 and 65 below — is a
        ///     1-aligned type, so the threshold-edge fixtures are byte members and are generated rather than
        ///     typed out. That IS the layout rule, not a way around it: a struct rounds up to its own alignment.
        /// </remarks>
        private static string BytesClass(int count)
        {
            var members = string.Join(
                " ",
                Enumerable.Range(0, count).Select(i => $"public byte B{i} {{ get; set; }}"));
            return "namespace T { public sealed class Dto { " + members + " } }";
        }

        // ─── Eligible: the shapes the feature exists for ─────────────────────────

        /// <summary>
        ///     The baseline. <c>{int, long}</c> is 16 B: the int at 0, the long at 8 because it is 8-aligned.
        ///     Under the 32 B line, so no <c>in</c> suggestion, and nothing was estimated.
        /// </summary>
        [Fact]
        public void Eligible_for_a_plain_dto_of_value_members()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int Id { get; set; } public long Value { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(16, verdict.Size);
            Assert.False(verdict.SuggestIn);
            Assert.False(verdict.SizeIsUpperBound);
            Assert.Empty(verdict.Reason);
        }

        /// <summary>
        ///     Plain fields are members too — the plan permits "auto-property/field", and a DTO written with
        ///     public fields is the same data with less ceremony.
        /// </summary>
        [Fact]
        public void Eligible_for_a_dto_declared_with_public_fields()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int Id; public long Value; } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(16, verdict.Size);
        }

        /// <summary>
        ///     A <c>string</c>, an array and a <c>List&lt;T&gt;</c> are permitted members: they stay reference
        ///     FIELDS in the would-be struct, which costs the root blit and not the eligibility (spec §10, and
        ///     the measured 0.21× / 0.07× string-leaf row in §9). Their 8 B is the x64 width, so the size is
        ///     flagged an upper bound and T2.2 must not print it as exact.
        /// </summary>
        [Fact]
        public void Eligible_for_reference_members_with_the_size_marked_an_upper_bound()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public string Name { get; set; } public string[] Tags { get; set; } public System.Collections.Generic.List<int> Ids { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(24, verdict.Size);
            Assert.True(verdict.SizeIsUpperBound);
        }

        /// <summary>
        ///     The commonest transfer model there is, and the one the controller's original pointer would have
        ///     refused: <c>Guid</c> is 16 B but 4-ALIGNED, so the 8-aligned reference lands at 16 and the struct
        ///     is 24 B, not 32.
        /// </summary>
        [Fact]
        public void Eligible_for_a_guid_keyed_dto_with_a_string()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public System.Guid Id { get; set; } public string Name { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(24, verdict.Size);
            Assert.True(verdict.SizeIsUpperBound);
        }

        /// <summary>
        ///     A 1:1 nested transfer model is INLINE in the would-be struct, not a reference — T2.3 rewrites it
        ///     transitively, so by the time the outer type is a struct the inner one is too (spec §8b table,
        ///     row 1). <c>Inner</c> is 8 B / 4-aligned, so the outer is long(0..8) + Inner(8..16) = 16 B, and
        ///     nothing was estimated: an inline struct is exact.
        /// </summary>
        [Fact]
        public void Eligible_for_a_nested_transfer_model_counted_inline()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Inner { public int A { get; set; } public int B { get; set; } } public sealed class Dto { public long L { get; set; } public Inner I { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(16, verdict.Size);
            Assert.False(verdict.SizeIsUpperBound);
        }

        /// <summary>
        ///     An OPTIONAL nested model costs more than a required one and the classifier must not forget it:
        ///     <c>Inner?</c> becomes <c>Nullable&lt;InnerStruct&gt;</c> — a flag padded up to the inner
        ///     alignment, then the inner value (spec §8b table, row 2). Inner is 8 B / 4-aligned, so the
        ///     optional is 4 + 8 = 12 B, and the outer is long(0..8) + optional(8..20) rounded to 8 = 24 B.
        ///     Sizing it as the bare 8-byte inner would UNDER-count, which is the one direction an upper bound
        ///     may not err in.
        /// </summary>
        [Fact]
        public void Eligible_for_an_optional_nested_transfer_model_counted_as_a_nullable()
        {
            var verdict = ClassifyType(
                "#nullable enable\nnamespace T { public sealed class Inner { public int A { get; set; } public int B { get; set; } } public sealed class Dto { public long L { get; set; } public Inner? I { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(24, verdict.Size);
        }

        /// <summary>
        ///     <c>Nullable&lt;T&gt;</c> of a permitted value member is permitted, and is measured as the runtime
        ///     lays it out: <c>int?</c> is 8 B / 4-aligned (flag, 3 B pad, int), then the byte at 8, rounded to
        ///     12.
        /// </summary>
        [Fact]
        public void Eligible_for_a_nullable_value_member()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int? A { get; set; } public byte B { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(12, verdict.Size);
        }

        /// <summary>An enum member is its underlying primitive: <c>short</c> here, so 2 B beside a 4 B int.</summary>
        [Fact]
        public void Eligible_for_an_enum_member()
        {
            var verdict = ClassifyType(
                "namespace T { public enum E : short { A } public sealed class Dto { public int Id { get; set; } public E Kind { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(8, verdict.Size);
        }

        /// <summary>
        ///     A constructor that only assigns its parameters to properties is the shape a positional
        ///     <c>readonly record struct</c> already has — it carries no logic to lose.
        /// </summary>
        [Fact]
        public void Eligible_for_a_constructor_that_only_assigns_its_properties()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int Id { get; set; } public long Value { get; set; } public Dto(int id, long value) { this.Id = id; Value = value; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(16, verdict.Size);
        }

        /// <summary>
        ///     A <c>record class</c> of nothing but auto-properties IS transfer-model shaped. It reaches here
        ///     only because the member walk skips compiler-synthesised members: a record declares
        ///     <c>Equals</c>, <c>GetHashCode</c>, <c>ToString</c>, <c>PrintMembers</c>, <c>EqualityContract</c>,
        ///     <c>&lt;Clone&gt;$</c> and <c>Deconstruct</c> without the consumer typing one of them, and a
        ///     "no methods" rule that counted those would refuse every record ever written.
        /// </summary>
        [Fact]
        public void Eligible_for_a_positional_record_class()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed record class Dto(int Id, long Value); }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(16, verdict.Size);
        }

        /// <summary>
        ///     A record's synthesised <c>IEquatable&lt;Dto&gt;</c> is the ONE permitted interface, because the
        ///     target shape — a <c>readonly record struct</c> — synthesises exactly that itself. Nothing is
        ///     lost in the rewrite.
        /// </summary>
        [Fact]
        public void Eligible_for_a_type_implementing_IEquatable_of_itself()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto : System.IEquatable<Dto> { public int Id { get; set; } public bool Equals(Dto other) => true; } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
        }

        /// <summary>
        ///     Static members carry no per-instance state and no layout, so they do not disqualify anything —
        ///     a DTO with a <c>static Dto Empty</c> or a const is still nothing but data per instance.
        /// </summary>
        [Fact]
        public void Eligible_despite_static_members_and_constants()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public const int Version = 2; public static Dto Create() => new Dto(); public int Id { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(4, verdict.Size);
        }

        /// <summary>
        ///     "sealed OR no derived types" is a disjunction, and this is the arm that keeps it from being a
        ///     `sealed`-only rule: an ordinary unsealed DTO nobody derives from converts safely.
        /// </summary>
        [Fact]
        public void Eligible_for_an_unsealed_class_nobody_derives_from()
        {
            var verdict = ClassifyType(
                "namespace T { public class Dto { public int Id { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
        }

        // ─── The size thresholds ─────────────────────────────────────────────────

        /// <summary>
        ///     All four edges of the two thresholds, pinned from both sides: 32 is silent, 33 asks for
        ///     <c>in</c>, 64 still asks for <c>in</c>, 65 is <c>TooLarge</c>. An off-by-one in either
        ///     comparison moves exactly one of these rows.
        /// </summary>
        /// <remarks>
        ///     The expected outcome travels as a <see langword="bool" /> rather than as
        ///     <c>TransferModelShape.Outcome</c> because the classifier is internal to the generator: a public
        ///     xunit theory method cannot take a parameter of a less accessible type (CS0051).
        /// </remarks>
        [Theory]
        [InlineData(32, false, false)]
        [InlineData(33, false, true)]
        [InlineData(64, false, true)]
        [InlineData(65, true, false)]
        public void Size_thresholds_hold_at_both_edges(int bytes, bool tooLarge, bool suggestIn)
        {
            var verdict = ClassifyType(BytesClass(bytes));

            Assert.Equal(
                tooLarge ? TransferModelShape.Outcome.TooLarge : TransferModelShape.Outcome.Eligible,
                verdict.Kind);
            Assert.Equal(bytes, verdict.Size);
            Assert.Equal(suggestIn, verdict.SuggestIn);
        }

        // ─── Refusals ────────────────────────────────────────────────────────────

        /// <summary>Nothing to suggest: it is already a value type.</summary>
        [Fact]
        public void Refuses_a_type_that_is_already_a_value_type()
        {
            var verdict = ClassifyType("namespace T { public struct Dto { public int Id; } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("already a value type", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     An abstract class is a base by definition and a struct cannot be one. Refused ahead of the
        ///     derived-type sweep so the reason says the thing the consumer can see in the declaration.
        /// </summary>
        [Fact]
        public void Refuses_an_abstract_class()
        {
            var verdict = ClassifyType("namespace T { public abstract class Dto { public int Id { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("abstract", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     An unsealed class someone DERIVES from cannot become a struct: the derived type has nowhere to
        ///     go. The reason names the derived type, because that is the file the consumer must look at.
        /// </summary>
        [Fact]
        public void Refuses_an_unsealed_class_with_a_derived_type_in_the_compilation()
        {
            var verdict = ClassifyType(
                "namespace T { public class Dto { public int Id { get; set; } } public sealed class Special : Dto { } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("Special", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A base class other than <c>object</c> is inherited state and behaviour a struct cannot carry.
        ///     Distinct from the rule above — this is the DERIVED side of the same hierarchy, and it is refused
        ///     even when the type is sealed.
        /// </summary>
        [Fact]
        public void Refuses_a_class_with_a_base_class()
        {
            var verdict = ClassifyType(
                "namespace T { public class Root { public int A { get; set; } } public sealed class Dto : Root { public int Id { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("Root", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A constructor with logic — validation, normalisation, a chained call — is behaviour the rewrite
        ///     would silently keep running on a type whose default value now bypasses it entirely
        ///     (<c>default(Dto)</c> calls no constructor). Refused, and the check reads the BODY: the signature
        ///     of a validating constructor is indistinguishable from an assigning one.
        /// </summary>
        [Theory]
        [InlineData("public Dto(int id) { if (id < 0) throw new System.ArgumentException(nameof(id)); Id = id; }")]
        [InlineData("public Dto(int id) { Id = id + 1; }")]
        [InlineData("public Dto(int id) { Id = id; if (id > 0) { } }")]
        [InlineData("public Dto(int id) : this() { Id = id; } public Dto() { }")]
        public void Refuses_a_constructor_that_does_more_than_assign(string ctor)
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int Id { get; set; } " + ctor + " } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("constructor", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     An instance method is behaviour, and behaviour is what separates a transfer model from a domain
        ///     object. The reason names the method so the consumer knows what to move.
        /// </summary>
        [Fact]
        public void Refuses_an_instance_method()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int Id { get; set; } public int Double() => Id * 2; } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("Double", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A computed property is a method wearing a property's clothes — it has no backing field, so it is
        ///     not state and it is not layout. Refused for the same reason as a method, and separately tested
        ///     because it reaches the classifier as an <c>IPropertySymbol</c>, not an <c>IMethodSymbol</c>.
        /// </summary>
        [Fact]
        public void Refuses_a_computed_property()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int A { get; set; } public int B { get; set; } public int Total => A + B; } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("Total", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     An event is a subscription list bound to a particular INSTANCE. Copy the struct and the
        ///     subscribers travel with a copy nobody raises — silent, which is the failure class this project
        ///     refuses outright.
        /// </summary>
        [Fact]
        public void Refuses_a_type_with_an_event()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int Id { get; set; } public event System.EventHandler Changed; } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("Changed", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     <c>IDisposable</c> means the type owns something whose release is timed. A struct copied into a
        ///     collection is disposed once and used afterwards, or disposed twice — either way the ownership
        ///     the interface promises is gone.
        /// </summary>
        [Fact]
        public void Refuses_a_disposable_type()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto : System.IDisposable { public int Id { get; set; } public void Dispose() { } } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("IDisposable", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Any interface other than <c>IEquatable&lt;self&gt;</c> is refused: every call through it boxes,
        ///     which turns the allocation the whole feature removes back on — one per call instead of one per
        ///     object. <c>IEquatable&lt;SOMETHING ELSE&gt;</c> is refused too; it is not what a record struct
        ///     synthesises.
        /// </summary>
        [Theory]
        [InlineData("System.IComparable<Dto>", "public int CompareTo(Dto other) => 0;")]
        [InlineData("System.IEquatable<string>", "public bool Equals(string other) => false;")]
        public void Refuses_any_interface_other_than_IEquatable_of_itself(string iface, string member)
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto : " + iface + " { public int Id { get; set; } " + member + " } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("implements", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The EF heuristic, attribute half. An ORM tracks entities BY REFERENCE — change tracking, identity
        ///     resolution and lazy loading all depend on it — so turning one into a value type breaks the ORM,
        ///     not just the code. Matched by NAME (with and without the <c>Attribute</c> suffix) rather than by
        ///     type, so it fires without the generator referencing EF Core; refusing wrongly here is far cheaper
        ///     than suggesting a consumer decompose an entity.
        /// </summary>
        [Theory]
        [InlineData("[T.Table(\"orders\")] ", "")]
        [InlineData("[T.Owned] ", "")]
        [InlineData("", "[T.Key] ")]
        public void Refuses_an_entity_by_its_attributes(string onType, string onMember)
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class TableAttribute : System.Attribute { public TableAttribute(string n) { } } " +
                "public sealed class OwnedAttribute : System.Attribute { } " +
                "public sealed class KeyAttribute : System.Attribute { } " +
                onType + "public sealed class Dto { " + onMember + "public int Id { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("entity", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The EF heuristic, usage half: a type that appears as <c>DbSet&lt;T&gt;</c> anywhere in the
        ///     compilation is an entity even with no attribute on it — the fluent API configures those. The
        ///     stand-in <c>DbSet&lt;T&gt;</c> is declared in the fixture rather than referenced, because the
        ///     rule is "a type with this metadata name", which is what a consumer's EF reference resolves to.
        /// </summary>
        [Fact]
        public void Refuses_a_type_used_as_a_DbSet_in_the_compilation()
        {
            var verdict = ClassifyType(
                "namespace Microsoft.EntityFrameworkCore { public class DbSet<T> { } } " +
                "namespace T { public sealed class Dto { public int Id { get; set; } } " +
                "public class Ctx { public Microsoft.EntityFrameworkCore.DbSet<Dto> Rows { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("DbSet", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A member whose own type is not transfer-model shaped refuses the OUTER type: converting it would
        ///     leave a struct holding a reference to the very class it was supposed to decompose, which is the
        ///     worst of both — value semantics on the outside, shared mutable state one hop in. The reason names
        ///     the member AND carries the inner reason, so the consumer is told which type to look at and why.
        /// </summary>
        [Fact]
        public void Refuses_a_member_whose_type_is_not_transfer_model_shaped()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Order { public int Id { get; set; } public int Total() => Id; } " +
                "public sealed class Dto { public Order Line { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("Line", verdict.Reason, StringComparison.Ordinal);
            Assert.Contains("Order", verdict.Reason, StringComparison.Ordinal);
            Assert.Contains("Total", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A cycle through members has no value layout at all — a struct containing itself is CS0523 — and
        ///     the walk must stop rather than recurse until the stack does. Classes reach here because the
        ///     compiler accepts the cycle in the CLASS form the consumer wrote.
        /// </summary>
        [Fact]
        public void Refuses_a_cycle_between_transfer_models()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public Node Head { get; set; } } " +
                "public sealed class Node { public Dto Owner { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("cycle", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A self-referencing class is the one-hop form of the same thing, and the one a linked-list DTO
        ///     actually hits.
        /// </summary>
        [Fact]
        public void Refuses_a_self_referencing_class()
        {
            var verdict = ClassifyType(
                "#nullable enable\nnamespace T { public sealed class Dto { public int Id { get; set; } public Dto? Next { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("cycle", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A member this generator cannot size refuses the type, and does NOT default to zero — the rule
        ///     <c>LayoutHygiene</c> states for its own <see langword="null" />. <c>nint</c> is as wide as the
        ///     platform, so any size computed from it would describe the build machine;
        ///     <c>DateTimeOffset</c> is simply not on the four-entry fixed-layout list, and extending that list
        ///     is a promise about another type's internals that belongs to its own task.
        /// </summary>
        [Theory]
        [InlineData("nint")]
        [InlineData("System.DateTimeOffset")]
        public void Refuses_a_member_whose_bytes_cannot_be_proven(string memberType)
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int Id { get; set; } public " + memberType +
                " Handle { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("Handle", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     An open generic has no size — its members' widths depend on the type argument — so it is refused
        ///     at the type rather than reaching the member walk and blaming the type parameter.
        /// </summary>
        [Fact]
        public void Refuses_an_open_generic_type()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto<TValue> { public TValue Value { get; set; } } }",
                "Dto");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("generic", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A class with no instance state has nothing to carry, and a zero-byte struct is not the remedy for
        ///     it. Refused rather than reported as "0 B, eligible", which would read as advice.
        /// </summary>
        [Fact]
        public void Refuses_a_class_with_no_instance_members()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public static int Count; } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("no instance members", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A record class with a hand-written method is refused like any other class with behaviour — the
        ///     <c>IsImplicitlyDeclared</c> filter skips what the compiler wrote, never what the consumer did.
        ///     The twin of <see cref="Eligible_for_a_positional_record_class" />: without this pair, a filter
        ///     that skipped every method would look correct.
        /// </summary>
        [Fact]
        public void Refuses_a_record_class_with_a_hand_written_method()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed record class Dto(int Id) { public int Twice() => Id * 2; } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("Twice", verdict.Reason, StringComparison.Ordinal);
        }

        // ─── The compilation-wide facts, and the shape T2.2 calls ────────────────

        /// <summary>
        ///     The overload T2.2 actually uses: the two compilation-wide sweeps are computed ONCE and handed in,
        ///     so <c>Classify</c> stays pure and cheap per mapped pair rather than hiding a lazy cache inside a
        ///     function that claims to be pure. Same verdicts either way, which is what makes the convenience
        ///     overload safe to keep for tests.
        /// </summary>
        [Fact]
        public void The_injected_facts_overload_gives_the_same_verdicts()
        {
            var (compilation, types) = Compile(
                "namespace Microsoft.EntityFrameworkCore { public class DbSet<T> { } } " +
                "namespace T { public sealed class Dto { public int Id { get; set; } } " +
                "public sealed class Entity { public int Id { get; set; } } " +
                "public class Ctx { public Microsoft.EntityFrameworkCore.DbSet<Entity> Rows { get; set; } } }");

            var facts = TransferModelShape.CompilationFacts.Gather(compilation);

            Assert.Equal(
                TransferModelShape.Outcome.Eligible,
                TransferModelShape.Classify(types["Dto"], compilation, facts).Kind);
            Assert.Equal(
                TransferModelShape.Outcome.NotEligible,
                TransferModelShape.Classify(types["Entity"], compilation, facts).Kind);
        }

        /// <summary>
        ///     The EF sweep is skipped wholesale when the compilation has no <c>DbSet&lt;T&gt;</c> to find,
        ///     which is most compilations — asserted as an observable fact (no entity is collected) rather than
        ///     left as a comment about a fast path nothing checks.
        /// </summary>
        [Fact]
        public void Gather_finds_no_entities_when_the_compilation_has_no_DbSet()
        {
            var (compilation, _) = Compile(
                "namespace T { public sealed class Dto { public int Id { get; set; } } " +
                "public class Ctx { public System.Collections.Generic.List<Dto> Rows { get; set; } } }");

            Assert.Equal(0, TransferModelShape.CompilationFacts.Gather(compilation).EntityCount);
        }
    }
}
