// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     The [MapTo] registry enumerates only <c>type.GetMembers()</c> — MapToGenerator.cs contains zero
///     <c>BaseType</c> references — while the [DwarfMapper] class model walks the base-type chain and
///     interfaces with name de-duplication. So inherited members are invisible to the registry:
///     an inherited DESTINATION member is silently never mapped (silent data loss, which this library's core
///     tenet forbids), and an inherited SOURCE member yields a DWARFR02 "unmapped" for a member that exists.
///     No test covered [MapTo] with inheritance before these.
/// </summary>
public class RegistryInheritanceTests
{
    // Dto.Id is inherited. The registry never enumerates it, so it is never assigned — silently.
    private const string InheritedDestinationMember = """
                                                      using DwarfMapper;
                                                      namespace Demo;
                                                      public class DtoBase { public int Id { get; set; } }
                                                      public class Dto : DtoBase { public string Name { get; set; } = ""; }
                                                      [MapTo(typeof(Dto))]
                                                      public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }
                                                      """;

    // Src.Id is inherited. The registry cannot see it, so Dto.Id looks unmapped and DWARFR02 fires wrongly.
    private const string InheritedSourceMember = """
                                                 using DwarfMapper;
                                                 namespace Demo;
                                                 public class SrcBase { public int Id { get; set; } }
                                                 [MapTo(typeof(Dto))]
                                                 public class Src : SrcBase { public string Name { get; set; } = ""; }
                                                 public class Dto { public int Id { get; set; } public string Name { get; set; } = ""; }
                                                 """;

    [Fact]
    public void An_inherited_destination_member_is_mapped()
    {
        var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(InheritedDestinationMember);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(generated.Contains("Id = source.Id,", StringComparison.Ordinal),
            "The inherited destination member 'Id' was never assigned, so it silently stays at its default. "
            + "The registry enumerates only type.GetMembers() and does not walk the base-type chain.\n\n"
            + "--- generated ---\n" + generated);
    }

    [Fact]
    public void An_inherited_source_member_does_not_produce_a_spurious_DWARFR02()
    {
        var diagnostics = GeneratorTestHarness.RunMapTo(InheritedSourceMember);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARFR02");
    }
}
