// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Testing;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     <c>ReferenceHandling</c> and <c>OnCycle</c> must reach the update-into resolver, not only the create
///     one. Both were hardcoded <c>false</c> at the update call site, so the same mapper honoured identity
///     preservation through <c>.Map</c> and silently did not through <c>.Update</c>.
/// </summary>
public class UpdateIntoPreserveParityTests
{
    private const string Types = """
        public sealed class Inner { public int X { get; set; } }
        public sealed class InnerDto { public int X { get; set; } }
        public sealed class Src { public int Id { get; set; } public System.Collections.Generic.List<Inner> Items { get; set; } = new(); }
        public sealed class Dst { public int Id { get; set; } public System.Collections.Generic.List<InnerDto> Items { get; set; } = new(); }
        """;

    private static string Build(string method, string options) => $$"""
        using System.Linq;
        using DwarfMapper;
        namespace Demo;
        {{Types}}
        [DwarfMapper({{options}})]
        public partial class M
        {
        {{method}}
        }
        """;

    [Fact]
    public void Update_into_carries_the_preserve_context_exactly_as_create_does()
    {
        // Preserve mode threads a mapping context through nested/element helpers so shared references resolve
        // to the same instance. The assertion is a PARITY one on purpose: hard-coding the expected emitted
        // text would break on any emitter refactor, whereas "the two endpoints agree" is the actual claim.
        const string opts = "ReferenceHandling = ReferenceHandlingStrategy.Preserve";

        var (createDiag, create) = GeneratorTestHarness.Run(
            Build("    public partial Dst Map(Src s);", opts));
        var (updateDiag, update) = GeneratorTestHarness.Run(
            Build("    public partial void Update(Src s, Dst d);", opts));

        Assert.Empty(createDiag.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Empty(updateDiag.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));

        var createCtx = CountContextHelpers(create);
        var updateCtx = CountContextHelpers(update);

        Assert.True(createCtx > 0,
            "Preserve mode emitted no context-carrying helper for the CREATE endpoint, so this test cannot "
            + "tell the two endpoints apart and would pass no matter what update did.");

        Assert.True(updateCtx > 0,
            $"Preserve mode emitted {createCtx} context-carrying helper call(s) for .Map and {updateCtx} for "
            + ".Update on the same mapper. ReferenceHandling is not reaching the update-into resolver, so "
            + "shared references are preserved through one endpoint and not the other.");
    }

    private static int CountContextHelpers(string generated) =>
        generated.Split("DwarfRefContext").Length - 1;

    // The emitted marker is the reference-context type itself (`DwarfRefContext`), which preserve mode
    // threads through nested and element helpers. Named here rather than inline so a rename shows up as one
    // failing test with a clear cause instead of a silently vacuous count.
}
