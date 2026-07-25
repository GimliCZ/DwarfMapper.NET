// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
/// Completeness audit for the generator's INTERNAL behaviour enums. <c>CollectionConverter.TargetKind</c> is
/// already gated (AssemblyScanTests.Scan6, TestTheTestsScanTests.T3b), but its siblings were not:
/// <list type="bullet">
///   <item><description><c>CollectionConverter.CountKind</c> (None/Length/Count) — drives capacity pre-sizing;</description></item>
///   <item><description><c>DictionaryConverter.DictTargetKind</c> — the dictionary target taxonomy;</description></item>
///   <item><description><c>DwarfMapper.Generator.Model.NullHandling</c> (None/ThrowIfNull/ValueOrDefault/NullableProject)
///   — each value emits distinct null-handling code.</description></item>
/// </list>
/// Each of these is reflected by metadata name (robust against accessibility) and every value must be
/// referenced in the generator's pipeline source. A dead enum value — added but wired to nothing — fails here,
/// the same standard TargetKind is held to.
/// </summary>
public class InternalEnumCoverageSelfValidationTests
{
    private static readonly Assembly GeneratorAssembly = typeof(DwarfGenerator).Assembly;

    private static string GeneratorSourceBlob()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate the repository root from the test output directory.");

        var genDir = Path.Combine(dir!.FullName, "src", "DwarfMapper.Generator");
        return string.Concat(
            Directory.EnumerateFiles(genDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                            && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText));
    }

    [Theory]
    [InlineData("DwarfMapper.Generator.Pipeline.CollectionConverter+CountKind")]
    [InlineData("DwarfMapper.Generator.Pipeline.DictionaryConverter+DictTargetKind")]
    [InlineData("DwarfMapper.Generator.Model.NullHandling")]
    public void Every_value_of_an_internal_behaviour_enum_is_referenced_in_generator_source(string metadataName)
    {
        var enumType = GeneratorAssembly.GetType(metadataName);
        Assert.True(enumType is { IsEnum: true },
            $"Internal enum '{metadataName}' not found in the generator assembly — the taxonomy moved; update "
            + "this self-validation.");

        var source = GeneratorSourceBlob();
        var enumName = enumType!.Name;

        // A value is "used" when it is referenced as `EnumName.Value` — the qualified form the emitter/classifier
        // writes. Bare-name matching would pass vacuously (e.g. NullHandling.None vs any `None`).
        var missing = Enum.GetNames(enumType)
            .Where(v => !source.Contains($"{enumName}.{v}", StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            $"{enumName} value(s) never referenced in generator source (dead enum value — wired to nothing):\n  "
            + string.Join("\n  ", missing.Select(v => $"{enumName}.{v}")));
    }
}
