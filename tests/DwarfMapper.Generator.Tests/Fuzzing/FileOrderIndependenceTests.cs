// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Fuzzing;

/// <summary>
///     Generated output must not depend on the ORDER the compiler was handed the source files.
///     <para>
///         <c>ITypeSymbol.GetMembers()</c> has no documented ordering guarantee for a <c>partial</c> type
///         split across files — the members arrive grouped by declaration, and which declaration comes first
///         follows the compilation's syntax-tree order. The codebase already knows this: the H1 note in
///         <c>MapperExtractor.Projection.cs</c> records that a first-past-the-post member pick "for a partial
///         source type split across files is not stable, so two builds could emit different expression trees".
///     </para>
///     <para>
///         That hazard is invisible to every other test here, because they all build a compilation from ONE
///         syntax tree in ONE order. Real projects hand the compiler files in whatever order MSBuild globs
///         them, which differs across filesystems and platforms — so this is the axis where "works on my
///         machine" is generated rather than merely observed.
///     </para>
/// </summary>
public class FileOrderIndependenceTests
{
    /// <summary>
    ///     A partial source type whose members are spread over three files, plus a partial target and the
    ///     mapper. Members are deliberately NOT in alphabetical order within each file, and the names collide
    ///     on prefix, so any residual dependence on declaration order shows up as reordered assignments rather
    ///     than being masked by a tidy input.
    /// </summary>
    private static string[] PartialSourceFiles() =>
    [
        """
        namespace Demo;
        public partial class Src { public int Zulu { get; set; } public string? Alpha { get; set; } }
        """,
        """
        namespace Demo;
        public partial class Src { public long Mike { get; set; } public string? Bravo { get; set; } }
        """,
        """
        namespace Demo;
        public partial class Src { public int Alpha2 { get; set; } public string? Yankee { get; set; } }
        """,
        """
        using DwarfMapper;
        namespace Demo;
        public class Dst
        {
            public int Zulu { get; set; }
            public string? Alpha { get; set; }
            public long Mike { get; set; }
            public string? Bravo { get; set; }
            public int Alpha2 { get; set; }
            public string? Yankee { get; set; }
        }

        [DwarfMapper]
        public partial class M { public partial Dst Map(Src s); }
        """
    ];

    private static string GenerateWith(IReadOnlyList<string> files)
    {
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(f)).ToArray();
        var compilation = CSharpCompilation.Create(
            "FileOrderAsm",
            trees,
            GeneratorTestHarness.BuildCompilation("Probe", "class __Probe {}").References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        // Concatenate only the GENERATED trees, ordered by hint name so the comparison is about content
        // rather than about the order the driver happened to add them.
        return string.Join("\n// ---- file boundary ----\n",
            output.SyntaxTrees
                .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
                .OrderBy(t => t.FilePath, StringComparer.Ordinal)
                .Select(t => t.ToString()));
    }

    [Fact]
    public void Emitted_output_is_identical_across_every_permutation_of_the_source_files()
    {
        var files = PartialSourceFiles();
        var baseline = GenerateWith(files);

        Assert.False(string.IsNullOrWhiteSpace(baseline),
            "No generated output — the permutation comparison would pass over nothing.");

        var differing = new List<string>();
        var count = 0;

        foreach (var permutation in Permutations(Enumerable.Range(0, files.Length).ToArray()))
        {
            count++;
            var ordered = permutation.Select(i => files[i]).ToList();
            if (!string.Equals(GenerateWith(ordered), baseline, StringComparison.Ordinal))
                differing.Add("[" + string.Join(",", permutation) + "]");
        }

        Assert.True(count >= 24, $"Expected all 4! = 24 permutations, ran {count}.");
        Assert.True(differing.Count == 0,
            "Generated output depends on the order source files were compiled — two machines globbing files "
            + "differently would produce different assemblies. Offending orderings: "
            + string.Join(" ", differing)
            + "\nLikely cause: a first-past-the-post member pick over GetMembers(), which is not ordered for a "
            + "partial type split across files (see the H1 note in MapperExtractor.Projection.cs).");
    }

    [Fact]
    public void The_permutation_harness_can_detect_a_difference()
    {
        // Negative control: prove GenerateWith actually varies with input, so a bug that made it return a
        // constant (or empty) string could not make the test above pass vacuously.
        var files = PartialSourceFiles().ToList();
        var baseline = GenerateWith(files);

        var altered = files.ToList();
        altered[0] = altered[0].Replace("Zulu", "Zulu9", StringComparison.Ordinal);
        var alteredTarget = altered[3].Replace("public int Zulu { get; set; }",
            "public int Zulu9 { get; set; }", StringComparison.Ordinal);
        altered[3] = alteredTarget;

        Assert.NotEqual(baseline, GenerateWith(altered));
    }

    private static IEnumerable<int[]> Permutations(int[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Length; i++)
        {
            var head = items[i];
            var rest = items.Where((_, idx) => idx != i).ToArray();
            foreach (var tail in Permutations(rest))
                yield return new[] { head }.Concat(tail).ToArray();
        }
    }
}
