// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     Round 31 T17 (research P5c): measured on EF Core 10.0.9, a call to a generated <c>Project(db.Orders)</c> is not
    ///     precompiled ("Dynamic LINQ queries are not supported when precompiling queries"), while
    ///     <c>db.Orders.Select(OrderMapper.ProjectExpression)</c> is — and under NativeAOT an un-precompiled query fails
    ///     when it first runs. DWARF115 says so at the projection method, only when BOTH PublishAot is set and EF Core is
    ///     referenced. EF Core is stood in for by an empty assembly of that name: detection reads the reference, not a type.
    /// </summary>
    public sealed class EfAotProjectionTests
    {
        private const string Src = """
            #nullable enable
            using System.Linq;
            using DwarfMapper;
            namespace T17;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } }
            [DwarfMapper] public partial class M
            {
                public partial IQueryable<D> Project(IQueryable<S> q);
                public partial D Map(S s);
            }
            """;

        private static readonly MetadataReference FakeEfCore = CSharpCompilation.Create(
                "Microsoft.EntityFrameworkCore",
                [CSharpSyntaxTree.ParseText("namespace Microsoft.EntityFrameworkCore { public class DbContext { } }")],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .ToMetadataReference();

        [Theory]
        [InlineData("true", true, true)]
        [InlineData("True", true, true)]
        [InlineData("false", true, false)]
        [InlineData(null, true, false)]
        [InlineData("true", false, false)]
        public void DWARF115_fires_only_for_NativeAOT_with_EF_Core(string? publishAot, bool referencesEf, bool expected)
        {
            var hits = Run(publishAot, referencesEf).Where(d => string.Equals(d.Id, "DWARF115", StringComparison.Ordinal)).ToList();
            if (!expected)
            {
                Assert.Empty(hits);
                return;
            }

            // Exactly the projection, not the plain Map beside it.
            var hit = Assert.Single(hits);
            var message = hit.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains("Projection 'Project' on 'M'", message, StringComparison.Ordinal);
            Assert.Contains("Select(M.ProjectExpression)", message, StringComparison.Ordinal);
            Assert.Equal(DiagnosticSeverity.Warning, hit.Severity);
        }

        private static ImmutableArray<Diagnostic> Run(string? publishAot, bool referencesEf)
        {
            var compilation = GeneratorTestHarness.BuildCompilation("T17", Src, NullableContextOptions.Enable);
            if (referencesEf)
            {
                compilation = compilation.AddReferences(FakeEfCore);
            }

            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            if (publishAot is not null)
            {
                options["build_property.PublishAot"] = publishAot;
            }

            var driver = CSharpGeneratorDriver.Create([new DwarfGenerator().AsSourceGenerator()],
                optionsProvider: new GlobalOptionsProvider(options));
            driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
            return diagnostics;
        }

        private sealed class GlobalOptionsProvider(Dictionary<string, string> global) : AnalyzerConfigOptionsProvider
        {
            public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(global);

            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
            {
                return new Options([]);
            }

            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
            {
                return new Options([]);
            }
        }

        private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                return values.TryGetValue(key, out value);
            }
        }
    }
}
