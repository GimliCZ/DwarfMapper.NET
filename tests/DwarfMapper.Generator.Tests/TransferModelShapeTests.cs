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
            var refs = References();

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

        private static IEnumerable<MetadataReference> References()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .Append(MetadataReference.CreateFromFile(typeof(DwarfMapperAttribute).Assembly.Location));
        }

        /// <summary>
        ///     Compiles <paramref name="librarySource" /> to an assembly, references THAT, and returns the
        ///     named type as the consuming compilation sees it â€” an imported symbol, not a source one.
        /// </summary>
        /// <remarks>
        ///     The difference is the whole point: a metadata symbol has no <c>DeclaringSyntaxReferences</c>
        ///     and, under the default <c>MetadataImportOptions</c>, no private backing fields either. Rules
        ///     that read syntax do not FAIL on such a symbol, they pass it, so nothing short of a real
        ///     metadata reference tests them. Building one in-memory is a few lines and is the only honest
        ///     fixture for this.
        /// </remarks>
        private static (Compilation Compilation, INamedTypeSymbol Type) CompileFromMetadata(
            string librarySource,
            string typeMetadataName)
        {
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
            var library = CSharpCompilation.Create(
                "TransferModelLib_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(librarySource)
                },
                References(),
                options);

            using var image = new MemoryStream();
            var emitted = library.Emit(image);
            Assert.True(emitted.Success, "library fixture did not compile: " + string.Join("; ", emitted.Diagnostics));
            image.Position = 0;

            var consumer = CSharpCompilation.Create(
                "TransferModelConsumer_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText("namespace Consumer { public sealed class Anchor { public int A; } }")
                },
                References().Append(MetadataReference.CreateFromStream(image)),
                options);

            var type = consumer.GetTypeByMetadataName(typeMetadataName)
                       ?? throw new InvalidOperationException($"'{typeMetadataName}' is not in the emitted assembly");

            return (consumer, type);
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
                "#nullable enable\nnamespace T { public sealed class Inner { public int A { get; set; } public int B { get; set; } } public sealed class Dto { public long L { get; set; } public Inner I { get; set; } } }");

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
        ///     Self-review finding, round 29 T2.1: with the nullable context OFF — which is where a great deal
        ///     of existing DTO code lives — <c>Inner I</c> may legally hold null, so its value form has to be
        ///     <c>Nullable&lt;InnerStruct&gt;</c> and costs the flag. Reading only
        ///     <c>NullableAnnotation.Annotated</c> counted this member as the bare inline struct (16 B) and
        ///     reported a struct SMALLER than the consumer would get, which is the one direction an upper bound
        ///     may not err in. The twin of <see cref="Eligible_for_a_nested_transfer_model_counted_inline" />,
        ///     which is the same shape with the context on and the member explicitly non-null.
        /// </summary>
        [Fact]
        public void An_oblivious_nested_model_is_costed_as_optional_not_inline()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Inner { public int A { get; set; } public int B { get; set; } } public sealed class Dto { public long L { get; set; } public Inner I { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(24, verdict.Size);
        }

        /// <summary>
        ///     Self-review finding, round 29 T2.1: <c>LayoutHygiene</c> refuses a struct whose fields are split
        ///     across partial declarations because the compiler defines no field order for it (CS0282) — and
        ///     the classifier, which RETURNS a size, was accepting exactly that shape for a class and reporting
        ///     a number computed from whichever order the symbol walk produced. Same refusal, same wording as
        ///     <c>BlittableProof</c>'s, so a consumer meets one sentence for one situation.
        /// </summary>
        [Fact]
        public void Refuses_a_class_whose_fields_span_partial_declarations()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed partial class Dto { public byte A { get; set; } } " +
                "public sealed partial class Dto { public long B { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("partial declaration", verdict.Reason, StringComparison.Ordinal);
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
        ///     <b>The advice must compile.</b> Round 29 T2.3 measured what a <c>readonly record struct</c>
        ///     actually stands aside for and found the classifier's stated rule true of three of the four
        ///     equality-and-printing members and false of the fourth. <c>Equals(T)</c>, <c>GetHashCode()</c> and
        ///     <c>ToString()</c> may all be hand-written in a record struct; the synthesised
        ///     <c>override bool Equals(object?)</c> is emitted unconditionally, so a hand-written one is CS0111
        ///     — a compile error in the consumer's own file, produced by following <c>DWARF103</c>'s advice.
        ///     <para>
        ///         <c>operator ==</c> and <c>operator !=</c> are the same collision reached through a hole in
        ///         the walk rather than a hole in the rule: they are STATIC, and the member loop's
        ///         <c>IsStatic</c> skip meant no rule ever looked at them. The check now runs before that skip.
        ///     </para>
        /// </summary>
        [Theory]
        [InlineData(
            "public override bool Equals(object o) => false; public override int GetHashCode() => Id;",
            "Equals(object)")]
        [InlineData(
            "public static bool operator ==(Dto a, Dto b) => true; public static bool operator !=(Dto a, Dto b) => false;",
            "operator ==")]
        public void Refuses_a_member_a_record_struct_would_collide_with(string members, string expected)
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int Id { get; set; } " + members + " } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains(expected, verdict.Reason, StringComparison.Ordinal);
            Assert.Contains("CS0111", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The other side of that measurement, so the refusal above cannot quietly widen into "any
        ///     hand-written equality member". These three a record struct really does stand aside for, and each
        ///     was compiled to confirm it rather than reasoned about.
        /// </summary>
        [Fact]
        public void Eligible_for_the_three_equality_members_a_record_struct_stands_aside_for()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int Id { get; set; } " +
                "public bool Equals(Dto o) => Id == o.Id; " +
                "public override int GetHashCode() => Id; " +
                "public override string ToString() => \"dto\"; } }");

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
        [InlineData("public Dto(int id) => Validate(id); private static void Validate(int id) { }")]
        [InlineData("public Dto(int id) { Id = 42; }")]
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
        ///     <b>And the attributes that are NOT on the list, which is the half nothing pinned.</b>
        ///     <c>docs/diagnostics.md</c> advertised <c>[Key]</c>/<c>[Table]</c>/<c>[Column]</c>/
        ///     <c>[ForeignKey]</c> while this classifier matched <c>Key</c>, <c>Table</c> and <c>Owned</c> — it
        ///     named two attributes the engine never checks and omitted one it does, so a consumer reading it
        ///     would have expected their <c>[Column]</c>-annotated type to be refused and got silence. The
        ///     documentation is now the code's list; these rows are what stops the two drifting apart again,
        ///     because the theory above could only ever catch the overclaim by failing to exist.
        ///     <para>
        ///         The rows are ELIGIBLE on purpose. Neither attribute says the type is TRACKED: <c>[Column]</c>
        ///         names a column and <c>[ForeignKey]</c> names a relationship, and both appear on plain
        ///         mapping types. Broadening a refusal is the risky direction and this project's rule is to
        ///         ship and then narrow on evidence — so if a later round finds <c>[ForeignKey]</c> strong
        ///         enough to add, this row moves rather than being deleted, and the docs move with it.
        ///     </para>
        /// </summary>
        [Theory]
        [InlineData("Column")]
        [InlineData("ForeignKey")]
        public void Does_not_refuse_an_entity_attribute_it_never_matched(string attribute)
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class " + attribute + "Attribute : System.Attribute { } " +
                "[" + attribute + "] public sealed class Dto { public int Id { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
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
            Assert.Contains("contains itself by value", verdict.Reason, StringComparison.Ordinal);
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
            Assert.Contains("contains itself by value", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A member this generator cannot size refuses the type, and does NOT default to zero — the rule
        ///     <c>LayoutHygiene</c> states for its own <see langword="null" />. <c>nint</c> is as wide as the
        ///     platform, so any size computed from it would describe the build machine; <c>System.Half</c> is
        ///     simply not on <c>LayoutHygiene.FixedLayoutBclSize</c>'s list, so its bytes are not knowable from
        ///     symbols alone.
        ///     <para>
        ///         <c>System.DateTimeOffset</c> stood in the second row until round 29 <c>T0.3c</c>, when the
        ///         representative corpus showed that this refusal CASCADES — one timestamp member made the whole
        ///         DTO unmeasurable and silenced <c>DWARF103</c> on it — and the table gained it, along with
        ///         <c>DateOnly</c> and <c>TimeOnly</c>. <c>Half</c> was measured in the same task and left out on
        ///         the first clause of that table's bar: nothing writes a <c>Half</c> into a transfer model, and
        ///         an entry nothing writes is a maintenance promise bought for nothing. It is the right fixture
        ///         for this row rather than the next one to fall, and if a later round adds it, this row moves to
        ///         another unwritten metadata struct instead of being deleted — refusal is still the default.
        ///     </para>
        /// </summary>
        [Theory]
        [InlineData("nint")]
        [InlineData("System.Half")]
        public void Refuses_a_member_whose_bytes_cannot_be_proven(string memberType)
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int Id { get; set; } public " + memberType +
                " Handle { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("Handle", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     <b>The other side of that refusal, and the one <c>DWARF103</c> actually rides on.</b> The rule
        ///     above cascades: an unmeasurable member refuses the whole ENCLOSING type, so before round 29
        ///     <c>T0.3c</c> one <c>DateTimeOffset CreatedAt</c> made an otherwise ordinary DTO
        ///     <c>NotEligible</c> and <c>DWARF103</c> went silent on it. This is the regression test for that,
        ///     and it belongs HERE rather than beside the <c>LayoutHygiene</c> cases: those measure a struct,
        ///     and <c>DWARF103</c>'s question is about a CLASS — the would-be struct <c>Classify</c> sizes,
        ///     which <c>LayoutHygiene.Measure</c> refuses outright and so cannot cover.
        ///     <para>
        ///         The nullable row is the one that would have been an easy half-fix. A <c>DateTimeOffset?</c>
        ///         reaches the table by a different path — <c>TryMeasureMember</c> hands it to
        ///         <c>LayoutHygiene.MeasureMember</c>, which recurses through the <c>Nullable&lt;T&gt;</c> arm
        ///         and <c>AsOptional</c> rather than looking the type up directly — and nothing in the consumer
        ///         corpus exercises it, because every corpus DTO carrying an optional timestamp is refused by an
        ///         earlier rule anyway. So the corpus could not have caught this row, and only this test does.
        ///     </para>
        ///     <para>
        ///         Sizes, so a failure reads as a layout change rather than a mystery: <c>{int, DateTimeOffset}</c>
        ///         is 24 bytes, <c>{int, DateTimeOffset?}</c> 32, <c>{int, TimeOnly}</c> 16, <c>{int, DateOnly}</c>
        ///         8 — all inside the 32-byte silent band, so all <c>Eligible</c>.
        ///     </para>
        /// </summary>
        [Theory]
        [InlineData("System.DateTimeOffset")]
        [InlineData("System.DateTimeOffset?")]
        [InlineData("System.TimeOnly")]
        [InlineData("System.DateOnly")]
        public void Accepts_a_member_whose_layout_the_table_knows(string memberType)
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int Id { get; set; } public " + memberType +
                " Handle { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
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
        ///     <b>And a fully CONSTRUCTED generic is refused too, which is the dangerous half.</b>
        ///     <c>Box&lt;int&gt;</c> has a perfectly knowable size, so nothing about measurement refuses it —
        ///     what refuses it is that the only declaration a rewrite can change is <c>Box&lt;T&gt;</c>.
        ///     Round 29 T2.3 built the shape and watched it happen: <c>DWARF103</c> said "Box&lt;int&gt; is 4
        ///     bytes, declare it a readonly record struct", and taking that advice converted a
        ///     <c>Box&lt;string&gt;</c> held elsewhere that nothing had classified, no diagnostic had named,
        ///     and whose real size is 8 — losing its reference identity silently, which is the change this
        ///     classifier exists to refuse.
        ///     <para>
        ///         Narrowed rather than left to the code fix because <c>DWARF103</c> has never shipped, so
        ///         nothing depends on it, and because a hint that says "this could be a struct" about a type
        ///         the fix would then decline to convert is advice known to be bad. The silenced report is a
        ///         non-case, not a real one waiting for another diagnostic.
        ///     </para>
        /// </summary>
        [Fact]
        public void Refuses_a_constructed_generic_type()
        {
            var (compilation, types) = Compile(
                "namespace T { public sealed class Box<TValue> { public TValue Value { get; set; } } " +
                "public sealed class Holder { public Box<int> Boxed { get; set; } } }");

            var constructed = (INamedTypeSymbol)types["Holder"].GetMembers("Boxed")
                .OfType<IPropertySymbol>().Single().Type;

            var verdict = TransferModelShape.Classify(constructed, compilation);

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("constructed generic", verdict.Reason, StringComparison.Ordinal);
            Assert.Contains("every other instantiation", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A non-generic type NESTED in a generic one is the same rule reached through the containing
        ///     chain: <c>Outer&lt;T&gt;.Inner</c> has arity 0 of its own, and rewriting it still edits a
        ///     declaration that every <c>Outer&lt;…&gt;</c> shares. Reading <c>TypeArguments</c> alone would
        ///     have missed it.
        /// </summary>
        [Fact]
        public void Refuses_a_type_nested_in_a_generic()
        {
            var (compilation, types) = Compile(
                "namespace T { public sealed class Outer<TValue> { public sealed class Inner " +
                "{ public int Id { get; set; } } } }");

            var verdict = TransferModelShape.Classify(types["Inner"], compilation);

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("generic", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     <b>The anti-drift lock between the classifier and the code fix.</b> The two express the same
        ///     rule in different currencies — this one walks symbols, <c>ConvertToRecordStructCodeFixProvider</c>
        ///     tests the <c>DocumentationCommentId</c> for a backtick — and they cannot share code, because
        ///     <c>DwarfMapper.CodeFixes</c> does not reference this assembly. So the agreement is asserted
        ///     instead: every shape refused here for being generic carries a backtick in its handle, and the
        ///     shape that is NOT refused does not.
        /// </summary>
        [Fact]
        public void The_generic_refusal_and_the_code_fixs_handle_test_agree()
        {
            var (compilation, types) = Compile(
                "namespace T { public sealed class Box<TValue> { public TValue Value { get; set; } } " +
                "public sealed class Outer<TValue> { public sealed class Inner { public int Id { get; set; } } } " +
                "public sealed class Plain { public int Id { get; set; } } " +
                "public sealed class Holder { public Box<int> Boxed { get; set; } } }");

            var constructed = (INamedTypeSymbol)types["Holder"].GetMembers("Boxed")
                .OfType<IPropertySymbol>().Single().Type;

            foreach (var generic in new[] { types["Box"], types["Outer"], types["Inner"], constructed })
            {
                Assert.False(TransferModelShape.Classify(generic, compilation).IsShaped, generic.Name);
                Assert.Contains(
                    "`",
                    generic.OriginalDefinition.GetDocumentationCommentId()!,
                    StringComparison.Ordinal);
            }

            Assert.True(TransferModelShape.Classify(types["Plain"], compilation).IsShaped);
            Assert.DoesNotContain(
                "`",
                types["Plain"].GetDocumentationCommentId()!,
                StringComparison.Ordinal);
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

        /// <summary>
        ///     Fix round 1, Critical: a type from a REFERENCED ASSEMBLY came back Eligible with three rules
        ///     silently vacuous. A metadata symbol has no <c>DeclaringSyntaxReferences</c>, so the constructor
        ///     walk found no body to object to and returned "allowed" for a constructor that validates; the
        ///     derived-type sweep reads this compilation's assembly and cannot see a subclass in the defining
        ///     one; and the code fix has no declaration to rewrite. One guard refuses all three, and the reason
        ///     names the assembly, because that project is the only place the consumer could act.
        /// </summary>
        [Fact]
        public void Refuses_a_type_imported_from_a_referenced_assembly()
        {
            var (compilation, type) = CompileFromMetadata(
                "namespace Lib { public sealed class Dto { public int Id { get; set; } public long Value { get; set; } " +
                "public Dto(int id) { if (id < 0) throw new System.ArgumentException(nameof(id)); Id = id; } } }",
                "Lib.Dto");

            var verdict = TransferModelShape.Classify(type, compilation);

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("referenced assembly", verdict.Reason, StringComparison.Ordinal);
            Assert.Equal(0, verdict.Size);
        }

        /// <summary>
        ///     The same root cause's second symptom, and worth its own test: an imported auto-property has no
        ///     backing field under the default <c>MetadataImportOptions</c>, so the member walk called an
        ///     ordinary DTO's property "computed". That reason is FALSE, and T2.2 prints the reason verbatim —
        ///     a wrong explanation is worse than a right refusal, because the consumer goes and stares at a
        ///     property that is exactly what it should be.
        /// </summary>
        [Fact]
        public void An_imported_auto_property_is_not_called_a_computed_property()
        {
            var (compilation, type) = CompileFromMetadata(
                "namespace Lib { public sealed class Plain { public int Id { get; set; } public long Value { get; set; } } }",
                "Lib.Plain");

            var verdict = TransferModelShape.Classify(type, compilation);

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.DoesNotContain("computed property", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Fix round 1: the estimate must PROPAGATE out of a nested model. <c>Inner</c> holds a string, so
        ///     its 16 B is a bound; inlining it into <c>Dto</c> makes <c>Dto</c>'s 24 B a bound too. Without
        ///     the propagation the outer verdict claims an exact size that was built partly from an estimate,
        ///     and T2.2 prints it as measured — the ruling-1 hazard, one level down, where no other fixture
        ///     was looking.
        /// </summary>
        [Fact]
        public void The_size_bound_propagates_out_of_a_nested_model()
        {
            var verdict = ClassifyType(
                "#nullable enable\nnamespace T { public sealed class Inner { public string Name { get; set; } = \"\"; public int A { get; set; } } " +
                "public sealed class Dto { public long L { get; set; } public Inner I { get; set; } = new Inner(); } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(24, verdict.Size);
            Assert.True(verdict.SizeIsUpperBound);
        }

        /// <summary>
        ///     Fix round 1: a collection's ELEMENT type is checked, and no fixture had exercised it. The
        ///     collection is a reference either way, but the code fix rewrites the reachable subgraph, and an
        ///     element it cannot rewrite is a member the consumer must be told about rather than one silently
        ///     left behind. The reason names the member AND the element's own reason.
        /// </summary>
        [Theory]
        [InlineData("System.Collections.Generic.List<Bad>")]
        [InlineData("Bad[]")]
        public void Refuses_a_collection_whose_element_is_not_transfer_model_shaped(string memberType)
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Bad { public int Id { get; set; } public int Twice() => Id * 2; } " +
                "public sealed class Dto { public " + memberType + " Items { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("Items", verdict.Reason, StringComparison.Ordinal);
            Assert.Contains("Twice", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Fix round 1, minor: <c>Dto[] Children</c> was refused as a reference cycle, which is factually
        ///     wrong — an array element is a REFERENCE, there is no layout recursion, and
        ///     <c>readonly record struct Dto(Dto[] Children)</c> is legal C#. The cycle rule is about a type
        ///     that contains ITSELF BY VALUE, which this shape does not.
        /// </summary>
        [Fact]
        public void A_self_referencing_array_member_is_not_a_cycle()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int Id { get; set; } public Dto[] Children { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(16, verdict.Size);
            Assert.True(verdict.SizeIsUpperBound);
        }

        /// <summary>
        ///     Fix round 1, minor: a struct member may be neither <c>protected</c> (CS0666 — there is no
        ///     derived type to protect it from) nor <c>virtual</c> (CS0106 — nothing can override it). Left
        ///     open, the T2.3 rewrite would put a compile error in the consumer's file instead of a diagnostic
        ///     in ours. A record's own protected virtual members are implicitly declared and never reach here,
        ///     which <see cref="Eligible_for_a_positional_record_class" /> keeps honest.
        /// </summary>
        [Theory]
        [InlineData("protected int Secret;", "protected")]
        [InlineData("public virtual int V { get; set; }", "virtual")]
        public void Refuses_a_member_a_struct_cannot_declare(string member, string expected)
        {
            var verdict = ClassifyType(
                "namespace T { public class Dto { public int Id { get; set; } " + member + " } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains(expected, verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>An indexer is not state, and a transfer model that computes on lookup is not one.</summary>
        [Fact]
        public void Refuses_an_indexer()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int Id { get; set; } public int this[int i] => Id + i; } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("indexer", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The "not a class" arm, reached by something that is not a struct either — every other refusal
        ///     test feeds it a class, and the value-type test covers the other side of that branch.
        /// </summary>
        [Fact]
        public void Refuses_an_interface()
        {
            var verdict = ClassifyType(
                "namespace T { public interface IDto { int Id { get; } } }",
                "IDto");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("not a class", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Ruling on the review's Important 3: a PUBLIC unsealed DTO can be subclassed by a consuming
        ///     project the sweep never sees, so the verdict records the scope of its own evidence rather than
        ///     letting T2.2 imply derivation was ruled out everywhere. Restricting the rule to non-public types
        ///     would end the feature, since most transfer models are public — so the check stays and the claim
        ///     gets qualified instead. Sealed and internal types are decided on complete evidence and carry no
        ///     flag.
        /// </summary>
        [Theory]
        [InlineData("public", true)]
        [InlineData("public sealed", false)]
        [InlineData("internal", false)]
        public void An_eligible_verdict_records_how_far_the_derivation_check_could_see(
            string modifiers,
            bool limited)
        {
            var verdict = ClassifyType(
                "namespace T { " + modifiers + " class Dto { public int Id { get; set; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(limited, verdict.DerivationCheckedWithinAssemblyOnly);
        }

        /// <summary>
        ///     A nested model over 64 B does NOT refuse its owner, and the controller ruled the code right:
        ///     TooLarge is a size BAND, not a shape. The inner type can perfectly well be a struct — it is
        ///     merely one to pass by <c>in</c> — so it inlines, and inlining it carries the outer past 64,
        ///     where the outer's own threshold reports it honestly. Nine longs is 72 B, and the outer comes
        ///     back 72 B rather than refused.
        /// </summary>
        [Fact]
        public void A_nested_model_over_the_limit_makes_the_outer_too_large_not_refused()
        {
            var longs = string.Join(
                " ",
                Enumerable.Range(0, 9).Select(i => $"public long L{i} {{ get; set; }}"));
            var verdict = ClassifyType(
                "#nullable enable\nnamespace T { public sealed class Inner { " + longs + " } " +
                "public sealed class Dto { public Inner I { get; set; } = new Inner(); } }");

            Assert.Equal(TransferModelShape.Outcome.TooLarge, verdict.Kind);
            Assert.Equal(72, verdict.Size);
        }

        // ─── The fail-safe default (T2.3 ruling 3) ───────────────────────────────

        /// <summary>
        ///     <c>default(Verdict)</c> must REFUSE. <c>Outcome.Eligible</c> was the enum's zero member until
        ///     round 29 T2.3, so a verdict nobody had computed answered <c>IsShaped</c> true — "yes, rewrite
        ///     this consumer's class into a struct" as the value of a struct that had never been asked. Two
        ///     separate agents hit it: T2.2's implementer wrote <c>default(Verdict)</c> for "there is no source
        ///     verdict" and the block-copy clause came straight back. T2.3 consumes verdicts to decide what a
        ///     code fix REWRITES, so the default answer has to be the safe one by construction rather than by
        ///     every caller remembering.
        /// </summary>
        [Fact]
        public void A_default_verdict_is_not_shaped()
        {
            Assert.Equal(TransferModelShape.Outcome.NotEligible, default(TransferModelShape.Verdict).Kind);
            Assert.False(default(TransferModelShape.Verdict).IsShaped);
        }

        /// <summary>
        ///     The same fact stated where it actually bites: the zero of the ENUM, which is what a
        ///     default-constructed <c>Verdict</c>, a zeroed array element and an uninitialised field all get.
        /// </summary>
        [Fact]
        public void The_refusal_outcome_is_the_enums_zero_member()
        {
            Assert.Equal(TransferModelShape.Outcome.NotEligible, default(TransferModelShape.Outcome));
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

        /// <summary>
        ///     The DbSet sweep walks every member of every type, and a context also declares members that carry no
        ///     type to test: an event and a nested class. They are passed over, and the DbSet beside them still marks
        ///     its entity.
        /// </summary>
        [Fact]
        public void The_DbSet_sweep_passes_over_members_that_are_not_properties_fields_or_methods()
        {
            var verdict = ClassifyType(
                "namespace Microsoft.EntityFrameworkCore { public class DbSet<T> { } } " +
                "namespace T { public sealed class Dto { public int Id { get; set; } } " +
                "public class Ctx { public Microsoft.EntityFrameworkCore.DbSet<Dto> Rows { get; set; } public event System.Action Changed; public class Nested { } } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Equal("'Dto' is used as a 'DbSet<Dto>' in this compilation, so it is an ORM entity", verdict.Reason);
        }

        // ─── Refusals and passes the classifier reached only through these fixtures ────────

        /// <summary>A static class holds no instance data, so there is nothing to lay out as a struct.</summary>
        [Fact]
        public void Refused_for_a_static_class()
        {
            var verdict = ClassifyType("namespace T { public static class Dto { public static int A; } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Equal("'Dto' is a static class and holds no instance data", verdict.Reason);
        }

        /// <summary>
        ///     A chain of nested models deeper than the classifier's recursion cap. It is not a cycle, so only the cap
        ///     stops the walk, and the refusal names the cap rather than a layout rule.
        /// </summary>
        [Fact]
        public void Refused_for_nested_models_deeper_than_the_recursion_cap()
        {
            var source = new System.Text.StringBuilder("#nullable enable\nnamespace T {\n");
            for (var i = 0; i < 18; i++)
                source.Append("public sealed class C").Append(i).Append(" { public C").Append(i + 1).Append(" I { get; set; } }\n");
            source.Append("public sealed class C18 { public int A { get; set; } }\n}\n");

            var verdict = ClassifyType(source.ToString(), "C0");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Contains("nests transfer models more than 16 deep", verdict.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     C# requires <c>==</c> and <c>!=</c> together, and the refusal names whichever the class declares first.
        ///     Every other fixture declared <c>==</c> first.
        /// </summary>
        [Fact]
        public void Refused_for_operator_inequality_declared_first_and_named_as_such()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int A { get; set; } " +
                "public static bool operator !=(Dto x, Dto y) => false; public static bool operator ==(Dto x, Dto y) => true; " +
                "public override bool Equals(object o) => false; public override int GetHashCode() => 0; } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Equal(
                "'Dto' declares 'operator !=', which a readonly record struct synthesises and cannot have declared beside it (CS0111)",
                verdict.Reason);
        }

        /// <summary>
        ///     An operator other than equality does not collide with anything a record struct synthesises, so it is a
        ///     static member like any other and does not refuse the type.
        /// </summary>
        [Fact]
        public void Eligible_with_an_operator_other_than_equality()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed class Dto { public int A { get; set; } public static Dto operator +(Dto x, Dto y) => x; } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(4, verdict.Size);
        }

        /// <summary>An expression-bodied constructor that only assigns its parameter carries no logic.</summary>
        [Fact]
        public void Eligible_with_an_expression_bodied_constructor_that_assigns_its_parameter()
        {
            var verdict = ClassifyType("namespace T { public sealed class Dto { public Dto(int a) => A = a; public int A { get; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(4, verdict.Size);
        }

        /// <summary>An expression-bodied constructor that computes the value is logic a struct rewrite would lose.</summary>
        [Fact]
        public void Refused_for_an_expression_bodied_constructor_that_computes_its_value()
        {
            var verdict = ClassifyType("namespace T { public sealed class Dto { public Dto(int a) => A = a + 1; public int A { get; } } }");

            Assert.Equal(TransferModelShape.Outcome.NotEligible, verdict.Kind);
            Assert.Equal("the constructor of 'Dto' does more than assign its members", verdict.Reason);
        }

        /// <summary>
        ///     A partial constructor's defining declaration has no body, and the implementing one carries it. The
        ///     bodiless half is passed over and the implementing half is the one judged.
        /// </summary>
        [Fact]
        public void Eligible_with_a_partial_constructor_whose_implementation_only_assigns()
        {
            var verdict = ClassifyType(
                "namespace T { public sealed partial class Dto { public partial Dto(int a); public partial Dto(int a) { A = a; } public int A { get; } } }");

            Assert.Equal(TransferModelShape.Outcome.Eligible, verdict.Kind);
            Assert.Equal(4, verdict.Size);
        }
    }
}
