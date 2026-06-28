// SPDX-License-Identifier: GPL-2.0-only
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// A [MapProperty(Use = ...)] converter must take precedence over DwarfMapper's automatic collection
/// mapping when the SOURCE member is a collection type. Regression for the AllowedOnPlatforms case:
/// HashSet&lt;T&gt; -> [Flags] enum via an explicit converter must NOT fall into collection routing (DWARF027).
/// </summary>
public class ConverterPrecedenceTests
{
    [Fact]
    public void Use_converter_takes_precedence_over_collection_mapping_for_collection_source()
    {
        const string s = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace Demo;

            [System.Flags]
            public enum PlatformFlags { None = 0, A = 1, B = 2 }

            public class Src { public HashSet<int> Platforms { get; set; } = new(); }
            public class Dst { public PlatformFlags Platforms { get; set; } }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            [MapProperty<Src, Dst>("Platforms", "Platforms", Use = nameof(ToFlags))]
            public partial class M
            {
                private static PlatformFlags ToFlags(HashSet<int> platforms)
                {
                    var f = PlatformFlags.None;
                    foreach (var p in platforms) f |= (PlatformFlags)p;
                    return f;
                }
            }
            """;

        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Use_converter_with_enum_element_and_self_map_present()
    {
        // Closer to the real AllowedOnPlatforms case: HashSet<enum> -> [Flags] enum, with a SELF-map of the
        // source on the same host (HashSet<enum> -> HashSet<enum>) and a second pair using the converter.
        const string s = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace Demo;

            public enum Platform { A, B, C }
            [System.Flags]
            public enum PlatformFlags { None = 0, A = 1, B = 2, C = 4 }

            public class Item { public HashSet<Platform> Platforms { get; set; } = new(); public string Name { get; set; } = ""; }
            public class Entity { public PlatformFlags Platforms { get; set; } public string Name { get; set; } = ""; }

            [DwarfMapper]
            [GenerateMap<Item, Entity>]
            [MapProperty<Item, Entity>("Platforms", "Platforms", Use = nameof(ToFlags))]
            [GenerateMap<Entity, Item>]
            [MapProperty<Entity, Item>("Platforms", "Platforms", Use = nameof(ToSet))]
            public partial class M
            {
                // Self-map of the source on the same host (shares source type with Item->Entity, so it is a
                // distinctly-named partial method, not [GenerateMap<Item,Item>]).
                public partial Item Clone(Item source);
                private static PlatformFlags ToFlags(HashSet<Platform> p) => PlatformFlags.None;
                private static HashSet<Platform> ToSet(PlatformFlags f) => new();
            }
            """;

        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Same_source_two_targets_via_named_partial_method_compiles()
    {
        // Real FusedChat shape (StoreItem -> {StoreItem clone, DbStore}): two maps share a SOURCE type.
        // DwarfMapper overloads by parameter type, so both as [GenerateMap] would emit two `Map(StoreItem)`
        // (illegal return-type overload -> CS0111). The supported pattern is a distinctly-named partial
        // method for the second target. This compiles the generated code to prove there is no collision.
        const string s = """
            using System;
            using System.Collections.Generic;
            using DwarfMapper;
            namespace Demo;

            [System.Flags] public enum AllowedFlag { None=1, A=2, B=4, C=8 }

            public class StoreItem {
                public required string ItemName { get; set; }
                public string? Response { get; set; }
                public HashSet<int> AllowedOnPlatforms { get; set; } = new();
            }
            public class DbStore {
                public required string ItemName { get; set; }
                public string? ResponseMessage { get; set; }
                public AllowedFlag AllowedOnPlatforms { get; set; }
            }

            [DwarfMapper]
            [GenerateMap<StoreItem, DbStore>]
            [MapProperty<StoreItem, DbStore>("Response", "ResponseMessage")]
            [MapProperty<StoreItem, DbStore>("AllowedOnPlatforms", "AllowedOnPlatforms", Use = nameof(ToFlag))]
            public partial class M {
                // StoreItem -> StoreItem (clone) shares its source with the map above: named partial method.
                public partial StoreItem Clone(StoreItem source);
                private static AllowedFlag ToFlag(HashSet<int> p) => AllowedFlag.None;
            }
            """;
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(s);
        Assert.DoesNotContain(errors, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Same_source_two_targets_via_GenerateMap_emits_DWARF060_not_raw_CS0111()
    {
        // Two [GenerateMap] from the SAME source to different targets would both emit `Map(Src)` —
        // overloading only by return type (CS0111). DWARF060 reports this loudly and the duplicate
        // emission is suppressed, so the consumer never sees the opaque generated-code CS0111.
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int V { get; set; } }
            public class A { public int V { get; set; } }
            public class B { public int V { get; set; } }

            [DwarfMapper]
            [GenerateMap<Src, A>]
            [GenerateMap<Src, B>]
            public partial class M { }
            """;
        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diags, d => d.Id == "DWARF060" && d.Severity == DiagnosticSeverity.Error);

        // Suppression: only one Map(Src) survives, so no raw CS0111 in the generated code.
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(s);
        Assert.DoesNotContain(errors, d => string.Equals(d.Id, "CS0111", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Use_converter_takes_precedence_for_collection_target()
    {
        // The reverse: [Flags] enum SOURCE -> HashSet<int> TARGET via an explicit converter.
        const string s = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace Demo;

            [System.Flags]
            public enum PlatformFlags { None = 0, A = 1, B = 2 }

            public class Src { public PlatformFlags Platforms { get; set; } }
            public class Dst { public HashSet<int> Platforms { get; set; } = new(); }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            [MapProperty<Src, Dst>("Platforms", "Platforms", Use = nameof(ToSet))]
            public partial class M
            {
                private static HashSet<int> ToSet(PlatformFlags flags) => new();
            }
            """;

        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }
}
