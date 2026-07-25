// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     ISSUE-020 — the registry's collection helper asked CollectionConverter.TryGetEnumerableElement for the
///     source's count kind and then discarded it (<c>out _</c>), so the buffer grew by repeated reallocation
///     even when the size was known up front. The class engine pre-sizes from that same helper.
/// </summary>
public class RegistryCollectionSizingTests
{
    [Fact]
    public void Known_count_source_pre_sizes_the_buffer()
    {
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public List<int> Xs { get; set; } = new(); }
                         public class Dto { public List<long> Xs { get; set; } = new(); }
                         """;
        var (_, generated) = GeneratorTestHarness.RunMapToWithSource(s);

        // List<T> is an ICollection<T>, so the count is known: the buffer must be constructed with it.
        Assert.Contains("List<long>(s.Count)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_count_source_still_emits_an_unsized_buffer()
    {
        // Over-reach guard: a bare IEnumerable<T> has no count, so no capacity argument may be emitted
        // (counting it would double-enumerate a possibly side-effecting sequence).
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public IEnumerable<int> Xs { get; set; } = new List<int>(); }
                         public class Dto { public List<long> Xs { get; set; } = new(); }
                         """;
        var (_, generated) = GeneratorTestHarness.RunMapToWithSource(s);

        Assert.Contains("List<long>()", generated, StringComparison.Ordinal);
    }
}
