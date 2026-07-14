// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Generic;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// Before/AfterMap hooks must fire EXACTLY ONCE per mapped node — including through the synthesized private
// helpers used for nested members and collection elements.
//
// This is the dual of a bug this library already shipped and fixed by hand: the synthesized helpers for
// nested/element maps did not replicate the pair's hooks, so hooks silently did NOT run (a no-op). The fix
// makes the helpers replicate the hooks — which opens the opposite failure: a helper that replicates them
// TWICE, or an element hook that also fires from the enclosing map. Nothing currently guards that direction,
// and a double-fired hook is exactly the kind of bug that is invisible unless you count.
//
// Counting is the whole point: an "it ran" assertion passes for both 1 and 2 invocations.

public sealed class HookNode
{
    public int V { get; set; }
}

public sealed class HookNodeDto
{
    public int V { get; set; }
}

public sealed class HookRoot
{
    public HookNode Nested { get; set; } = new();

    public List<HookNode> Items { get; set; } = new();
}

public sealed class HookRootDto
{
    public HookNodeDto Nested { get; set; } = new();

    public List<HookNodeDto> Items { get; set; } = new();
}

[DwarfMapper]
[GenerateMap<HookNode, HookNodeDto>]
[GenerateMap<HookRoot, HookRootDto>]
public partial class HookCountingMapper
{
    public int NodeBefore;
    public int NodeAfter;

    [BeforeMap]
    private void OnBeforeNode(HookNode source) => NodeBefore++;

    [AfterMap]
    private void OnAfterNode(HookNode source, HookNodeDto target) => NodeAfter++;
}

public class HookInvocationCountRuntimeTests
{
    [Fact]
    public void Hooks_fire_exactly_once_per_node_through_nested_and_element_helpers()
    {
        var mapper = new HookCountingMapper();
        var src = new HookRoot
        {
            Nested = new HookNode { V = 1 },
            Items = { new HookNode { V = 2 }, new HookNode { V = 3 }, new HookNode { V = 4 } },
        };

        var dto = mapper.Map(src);

        // 4 HookNode instances are mapped: 1 nested member + 3 collection elements.
        // Each hook must fire once per node — not zero (the original bug), not twice (its dual).
        Assert.Equal(4, mapper.NodeBefore);
        Assert.Equal(4, mapper.NodeAfter);

        // Sanity: the mapping itself is still correct.
        Assert.Equal(1, dto.Nested.V);
        Assert.Equal(new[] { 2, 3, 4 }, System.Linq.Enumerable.Select(dto.Items, i => i.V));
    }

    [Fact]
    public void Hooks_fire_once_on_a_direct_top_level_map()
    {
        var mapper = new HookCountingMapper();

        _ = mapper.Map(new HookNode { V = 7 });

        Assert.Equal(1, mapper.NodeBefore);
        Assert.Equal(1, mapper.NodeAfter);
    }
}
