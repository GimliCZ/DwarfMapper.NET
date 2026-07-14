// SPDX-License-Identifier: GPL-2.0-only
#nullable enable

using Xunit;

namespace DwarfMapper.IntegrationTests;

// Locks the RUNTIME semantics of a nullable-reference source mapped to a non-nullable target.
//
// DWARF070 suppresses the CS8601 that the generated raw assignment would otherwise leak into the consumer's
// build. That suppression must stay a *suppression*: it must not quietly become a throw, an empty string, or a
// skipped assignment. Changing observable behaviour in order to silence a warning would be exactly the kind of
// mapper-decides-for-you magic DwarfMapper exists to avoid. The null still flows — and the developer is told
// at BUILD time (DWARF070) instead of being surprised at run time.
//
// This fixture deliberately maps `string?` -> `string`, so it deliberately raises DWARF070; the project
// suppresses that id (see the csproj) precisely because pinning the un-fixed behaviour is the point here.

public sealed class NullRefSrc
{
    public string? Name { get; set; }
}

public sealed class NullRefDst
{
    public string Name { get; set; } = "untouched";
}

[DwarfMapper]
public partial class NullRefMapper
{
    public partial NullRefDst Map(NullRefSrc src);
}

public class NullableRefAssignRuntimeTests
{
    [Fact]
    public void Null_source_still_flows_through_to_the_non_nullable_target()
    {
        var result = new NullRefMapper().Map(new NullRefSrc { Name = null });

        // Not a throw, and not the destination's own initializer ("untouched"): the raw assign is preserved
        // verbatim. A null really is sitting in a `string` — which is exactly why DWARF070 warns at compile
        // time rather than the library silently picking a value for you.
        Assert.Null(result.Name);
    }

    [Fact]
    public void Non_null_source_maps_normally()
    {
        var result = new NullRefMapper().Map(new NullRefSrc { Name = "durin" });

        Assert.Equal("durin", result.Name);
    }
}
