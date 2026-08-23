// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Contract tests for the ambient cross-assembly mapping attributes: the generator-emitted manifests
    ///     (<see cref="DwarfProvidesMapAttribute" /> / <see cref="DwarfRequiresMapAttribute" />), the user-facing
    ///     consumption marker (<see cref="UsesMapAttribute" /> / <see cref="UsesMapAttribute{TSource,TDestination}" />),
    ///     and the validation-root marker (<see cref="DwarfMapperValidationRootAttribute" />). These pin their
    ///     AttributeUsage so the generator's emission/reading (later ambient-registry phases) can rely on it.
    /// </summary>
    public sealed class AmbientManifestAttributesTests
    {
        // ── DWARF086: the manifest is the generator's to write ────────────────────────────────────────────────

        private const string HandWrittenProvides = """
                                                   using DwarfMapper;
                                                   [assembly: DwarfProvidesMap(typeof(Demo.Src), typeof(Demo.Dst))]
                                                   namespace Demo;
                                                   public sealed class Src { public int Id { get; set; } }
                                                   public sealed class Dst { public int Id { get; set; } }
                                                   """;

        private const string OrdinaryMapper = """
                                              using DwarfMapper;
                                              namespace Demo;
                                              public sealed class Src { public int Id { get; set; } }
                                              public sealed class Dst { public int Id { get; set; } }
                                              [DwarfMapper]
                                              public partial class M { public partial Dst Map(Src s); }
                                              """;

        private static AttributeUsageAttribute Usage<T>() where T : Attribute
        {
            return (AttributeUsageAttribute)Attribute.GetCustomAttribute(typeof(T), typeof(AttributeUsageAttribute))!;
        }

        [Fact]
        public void ProvidesMap_and_RequiresMap_are_assembly_targeted_multiple()
        {
            foreach (var usage in new[]
                     {
                         Usage<DwarfProvidesMapAttribute>(), Usage<DwarfRequiresMapAttribute>()
                     })
            {
                Assert.Equal(AttributeTargets.Assembly, usage.ValidOn);
                Assert.True(usage.AllowMultiple);
                Assert.False(usage.Inherited);
            }

            var provides = new DwarfProvidesMapAttribute(typeof(int), typeof(string));
            Assert.Equal(typeof(int), provides.Source);
            Assert.Equal(typeof(string), provides.Destination);

            var requires = new DwarfRequiresMapAttribute(typeof(string), typeof(int));
            Assert.Equal(typeof(string), requires.Source);
            Assert.Equal(typeof(int), requires.Destination);
        }

        [Fact]
        public void UsesMap_generic_and_nongeneric_target_assembly_and_class()
        {
            var nonGeneric = Usage<UsesMapAttribute>();
            var generic = Usage<UsesMapAttribute<int, string>>();

            foreach (var usage in new[]
                     {
                         nonGeneric, generic
                     })
            {
                Assert.Equal(AttributeTargets.Assembly | AttributeTargets.Class, usage.ValidOn);
                Assert.True(usage.AllowMultiple);
            }

            var attr = new UsesMapAttribute(typeof(int), typeof(string));
            Assert.Equal(typeof(int), attr.Source);
            Assert.Equal(typeof(string), attr.Destination);
        }

        [Fact]
        public void ValidationRoot_is_single_assembly_marker_with_AutoValidate()
        {
            var usage = Usage<DwarfMapperValidationRootAttribute>();
            Assert.Equal(AttributeTargets.Assembly, usage.ValidOn);
            Assert.False(usage.AllowMultiple);

            var attr = new DwarfMapperValidationRootAttribute
            {
                AutoValidate = true
            };
            Assert.True(attr.AutoValidate);
            Assert.False(new DwarfMapperValidationRootAttribute().AutoValidate);
        }

        [Fact]
        public void Hand_written_DwarfProvidesMap_is_refused_with_DWARF086()
        {
            var (diagnostics, _) = GeneratorTestHarness.RunAll(HandWrittenProvides);

            var d = Assert.Single(diagnostics, x => string.Equals(x.Id, "DWARF086", StringComparison.Ordinal));
            var message = d.GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("emitted by the generator", message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DwarfProvidesMap", message, StringComparison.Ordinal);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        }

        [Fact]
        public void Hand_written_DwarfRequiresMap_is_refused_with_DWARF086()
        {
            // The Requires half carries a DIFFERENT remedy from the Provides half — [UsesMap] is how a consumer
            // declares a dependency the generator then records. A single message naming only the Provides remedy
            // would send this reader to the wrong attribute.
            const string src = """
                               using DwarfMapper;
                               [assembly: DwarfRequiresMap(typeof(Demo.Src), typeof(Demo.Dst))]
                               namespace Demo;
                               public sealed class Src { public int Id { get; set; } }
                               public sealed class Dst { public int Id { get; set; } }
                               """;

            var (diagnostics, _) = GeneratorTestHarness.RunAll(src);

            var d = Assert.Single(diagnostics, x => string.Equals(x.Id, "DWARF086", StringComparison.Ordinal));
            var message = d.GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("DwarfRequiresMap", message, StringComparison.Ordinal);
            Assert.Contains("[UsesMap", message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_refusal_points_at_the_hand_written_attribute()
        {
            // Location.None would put the error on the project rather than on the line the author wrote, which
            // for an assembly-level attribute is the difference between a fixable error and a scavenger hunt.
            var (diagnostics, _) = GeneratorTestHarness.RunAll(HandWrittenProvides);

            var d = Assert.Single(diagnostics, x => string.Equals(x.Id, "DWARF086", StringComparison.Ordinal));
            Assert.NotEqual(Location.None, d.Location);
            Assert.Equal(1, d.Location.GetLineSpan().StartLinePosition.Line);
        }

        [Fact]
        public void An_ordinary_mapper_compilation_does_not_trip_DWARF086()
        {
            var (diagnostics, _) = GeneratorTestHarness.RunAll(OrdinaryMapper);

            Assert.DoesNotContain(diagnostics,
                x => string.Equals(x.Id, "DWARF086", StringComparison.Ordinal));
        }

        /// <summary>
        ///     The negative control that actually exercises the discriminator.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <see cref="An_ordinary_mapper_compilation_does_not_trip_DWARF086" /> is the control the brief
        ///         asked for, and on its own it is worth very little: a generator never sees its OWN emissions in
        ///         <c>compilation.Assembly.GetAttributes()</c> — Roslyn hands it the pre-generation compilation, as
        ///         <c>AmbientValidator.ReadReferenced</c>'s own remarks say — so an unconditional refusal that
        ///         fired on every manifest entry in sight would still pass it.
        ///     </para>
        ///     <para>
        ///         So the manifest is put in front of the check on purpose: run the generator, take the OUTPUT
        ///         compilation (which does carry the emitted <c>[assembly: DwarfProvidesMap]</c>), and run the
        ///         generator over that. Without the discriminator this reports DWARF086 against the generator's own
        ///         line. The first assertion is the non-vacuity guard — if the manifest were not there, "no
        ///         DWARF086" would prove only that there was nothing to refuse.
        ///     </para>
        /// </remarks>
        [Fact]
        public void A_generator_emitted_manifest_does_not_trip_DWARF086()
        {
            var compilation = GeneratorTestHarness.BuildCompilation("DwarfMapperTestAsm", OrdinaryMapper);
            CSharpGeneratorDriver.Create(new DwarfGenerator())
                .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

            var emitted = output.Assembly.GetAttributes()
                .Where(a => a.AttributeClass?.Name == nameof(DwarfProvidesMapAttribute))
                .ToList();
            Assert.NotEmpty(emitted);
            Assert.All(emitted,
                a => Assert.EndsWith(".g.cs",
                    a.ApplicationSyntaxReference!.SyntaxTree.FilePath,
                    StringComparison.Ordinal));

            CSharpGeneratorDriver.Create(new DwarfGenerator())
                .RunGeneratorsAndUpdateCompilation(output, out _, out var second);

            Assert.DoesNotContain(second, x => string.Equals(x.Id, "DWARF086", StringComparison.Ordinal));
        }
    }
}
