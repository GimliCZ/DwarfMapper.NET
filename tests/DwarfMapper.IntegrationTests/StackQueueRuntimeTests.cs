// SPDX-License-Identifier: GPL-2.0-only
#nullable enable
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// Stack<T> / Queue<T> targets. Previously refused (DWARF027) to avoid a silently-reversed Stack; now supported
// with a defined, round-trip-safe ordering: ENUMERATING THE RESULT YIELDS THE SOURCE SEQUENCE. Queue is FIFO so
// that is natural; Stack is LIFO so the input is reversed on construction to keep its top-first enumeration in
// source order.

public sealed class SqSrc
{
    public List<int> Values { get; set; } = new();
}

public sealed class SqQueueDst
{
    public Queue<int> Values { get; set; } = new();
}

public sealed class SqStackDst
{
    public Stack<int> Values { get; set; } = new();
}

[DwarfMapper]
public partial class StackQueueMapper
{
    public partial SqQueueDst ToQueue(SqSrc s);
    public partial SqStackDst ToStack(SqSrc s);
}

public class StackQueueRuntimeTests
{
    [Fact]
    public void Queue_preserves_source_order_fifo()
    {
        var dto = new StackQueueMapper().ToQueue(new SqSrc { Values = new List<int> { 1, 2, 3 } });

        // Enumeration and dequeue both yield source order.
        Assert.Equal(new[] { 1, 2, 3 }, dto.Values.ToArray());
        Assert.Equal(1, dto.Values.Dequeue());
        Assert.Equal(2, dto.Values.Dequeue());
    }

    [Fact]
    public void Stack_enumeration_preserves_source_order()
    {
        var dto = new StackQueueMapper().ToStack(new SqSrc { Values = new List<int> { 1, 2, 3 } });

        // The whole point: enumerating the mapped Stack yields the SAME sequence as the source, not the
        // reversed [3,2,1] that `new Stack<int>(source)` would give.
        Assert.Equal(new[] { 1, 2, 3 }, dto.Values.ToArray());
    }

    [Fact]
    public void List_to_Stack_to_list_round_trips()
    {
        // The round-trip guarantee that motivates the ordering choice.
        var original = new List<int> { 10, 20, 30, 40 };
        var stack = new StackQueueMapper().ToStack(new SqSrc { Values = original });

        Assert.Equal(original, stack.Values.ToList());
    }

    [Fact]
    public void Empty_and_null_sources_produce_empty_collections()
    {
        var empty = new StackQueueMapper().ToStack(new SqSrc { Values = new List<int>() });
        Assert.Empty(empty.Values);

        // A null source collection maps to empty by default (NullCollectionStrategy.AsEmpty), never throws.
        var nullSrc = new StackQueueMapper().ToQueue(new SqSrc { Values = null! });
        Assert.Empty(nullSrc.Values);
    }
}
