// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;

namespace DwarfMapper.Generator.Tests.Golden;

public class GoldenCorpusShapeTests
{
    [Fact]
    public void Case_ids_are_unique_and_stable()
    {
        var cases = GoldenCorpus.Cases();
        var ids = cases.Select(c => c.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        // Running twice must produce the identical ordered id list — the manifest depends on it.
        Assert.Equal(ids, GoldenCorpus.Cases().Select(c => c.Id).ToList());
    }

    [Fact]
    public void Every_axis_contributes_cases()
    {
        var ids = GoldenCorpus.Cases().Select(c => c.Id).ToList();

        Assert.Contains(ids, id => id.StartsWith("cmb:", StringComparison.Ordinal));
        Assert.Contains(ids, id => id.StartsWith("syn:", StringComparison.Ordinal));
        Assert.Contains(ids, id => id.StartsWith("feat:", StringComparison.Ordinal));
    }

    [Fact]
    public void Both_generators_contribute_cases()
    {
        var byGenerator = GoldenCorpus.Cases().Select(c => c.GeneratorName).Distinct(StringComparer.Ordinal);

        Assert.Contains("DwarfGenerator", byGenerator, StringComparer.Ordinal);
        Assert.Contains("MapToGenerator", byGenerator, StringComparer.Ordinal);
    }
}
