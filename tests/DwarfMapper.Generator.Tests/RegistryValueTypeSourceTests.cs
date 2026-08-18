// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     <c>[MapTo]</c> on a <c>struct</c>. Its <c>AttributeUsage</c> admits
///     <c>AttributeTargets.Class | AttributeTargets.Struct</c> and the target check admits
///     <c>TypeKind.Struct</c>, so a value-type source is a legal placement — but the registry wrote
///     <c>if (source is null) throw …</c> into every extension method it emitted, without ever asking whether
///     the source could BE null. Against a non-nullable value type that pattern is <c>CS0037</c>, so every
///     such placement produced a generated file the compiler rejects: finding <b>A11-F1</b>, surfaced by the
///     surface matrix once it gained a Struct site, and invisible until then because no source in
///     <c>samples/</c> or <c>tests/</c> put <c>[MapTo]</c> on a struct.
///     <para>
///         Pinned in both directions here — a value-type source must LOSE the guard, a reference-type source
///         must KEEP it — because a fix that only deletes the guard passes the first half and silently breaks
///         the second. The third direction, a <c>Nullable&lt;T&gt;</c> source, cannot reach this generator at
///         all (an attribute sits on a declaration, and there is no declaration of <c>T?</c>) and is pinned on
///         the predicate itself in <see cref="Core.TypeFactsTests" />.
///     </para>
/// </summary>
public class RegistryValueTypeSourceTests
{
    private const string StructSource = """
                                        using DwarfMapper;
                                        namespace Demo;
                                        [MapTo(typeof(Dto))]
                                        public struct Src { public int Id { get; set; } public string Name { get; set; } }
                                        public class Dto { public int Id { get; set; } public string Name { get; set; } }
                                        """;

    private const string RecordStructSource = """
                                              using DwarfMapper;
                                              namespace Demo;
                                              [MapTo(typeof(Dto))]
                                              public record struct Src { public int Id { get; set; } public string Name { get; set; } }
                                              public class Dto { public int Id { get; set; } public string Name { get; set; } }
                                              """;

    private const string ClassSource = """
                                       using DwarfMapper;
                                       namespace Demo;
                                       [MapTo(typeof(Dto))]
                                       public class Src { public int Id { get; set; } public string Name { get; set; } }
                                       public class Dto { public int Id { get; set; } public string Name { get; set; } }
                                       """;

    [Theory]
    [InlineData(nameof(StructSource))]
    [InlineData(nameof(RecordStructSource))]
    public void A_value_type_source_emits_no_null_guard(string which)
    {
        var generated = GeneratorTestHarness.RunMapToWithSource(Pick(which)).GeneratedSource;

        Assert.DoesNotContain("source is null", generated, StringComparison.Ordinal);
        // The methods themselves must still be there — "no guard" says nothing unless something was emitted.
        Assert.Contains("MapTo<TTarget>(this global::Demo.Src source)", generated, StringComparison.Ordinal);
        Assert.Contains("ToDto(this global::Demo.Src source)", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(StructSource))]
    [InlineData(nameof(RecordStructSource))]
    public void A_value_type_source_produces_code_the_compiler_accepts(string which)
    {
        // The CS0037 that A11 measured, fourteen times over. Asserted against the FINAL compilation (the
        // user's source plus every generated file), which is the only place a defect in emitted text is
        // visible at all — the generator itself reported nothing and was right not to.
        GeneratorAssert.EmitsCompilableCode(Pick(which));
    }

    [Fact]
    public void A_reference_type_source_keeps_its_null_guard()
    {
        // The other direction. A class source is reachable from callers with no nullable annotations at all,
        // so the ArgumentNullException is contract rather than decoration — deleting the guard outright would
        // have turned the struct cells green and quietly removed it. Two methods, two guards.
        var generated = GeneratorTestHarness.RunMapToWithSource(ClassSource).GeneratedSource;

        Assert.Equal(2, CountOccurrences(generated, "if (source is null) throw"));
    }

    /// <summary>
    ///     A nested value-type member goes through the synthesized-helper path, which already HAD the
    ///     discrimination the extension methods lacked. It must still have it now that both read one
    ///     predicate — the regression a shared helper makes possible, and the reason this is pinned rather
    ///     than assumed.
    /// </summary>
    [Fact]
    public void A_nested_value_type_member_is_mapped_without_a_null_test()
    {
        const string source = """
                              using DwarfMapper;
                              namespace Demo;
                              [MapTo(typeof(Dto))]
                              public struct Src { public int Id { get; set; } public Leaf Node { get; set; } }
                              public struct Leaf { public int Value { get; set; } }
                              public class LeafDto { public int Value { get; set; } }
                              public class Dto { public int Id { get; set; } public LeafDto Node { get; set; } }
                              """;

        var (_, generated) = GeneratorTestHarness.RunMapToWithSource(source);

        Assert.Contains("__DwarfMapObj_", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("s is null ?", generated, StringComparison.Ordinal);
        GeneratorAssert.EmitsCompilableCode(source);
    }

    /// <summary>
    ///     A nested REFERENCE-type member still null-propagates. The same helper, the opposite answer,
    ///     recorded beside the case above so the pair reads as one contract instead of two coincidences.
    /// </summary>
    [Fact]
    public void A_nested_reference_type_member_still_null_propagates()
    {
        const string source = """
                              using DwarfMapper;
                              namespace Demo;
                              [MapTo(typeof(Dto))]
                              public struct Src { public int Id { get; set; } public Leaf Node { get; set; } }
                              public class Leaf { public int Value { get; set; } }
                              public class LeafDto { public int Value { get; set; } }
                              public class Dto { public int Id { get; set; } public LeafDto Node { get; set; } }
                              """;

        var (_, generated) = GeneratorTestHarness.RunMapToWithSource(source);

        Assert.Contains("s is null ?", generated, StringComparison.Ordinal);
        GeneratorAssert.EmitsCompilableCode(source);
    }

    private static string Pick(string which)
    {
        return which switch
        {
            nameof(StructSource) => StructSource,
            nameof(RecordStructSource) => RecordStructSource,
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, "No such fixture."),
        };
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }
}
