// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

/// <summary>
///     Direct pins for the <see cref="DwarfRefContext" /> on-stack guard API (<c>TryEnterNode</c> /
///     <c>ExitNode</c>), the SetNull-mode cycle breaker. The SetNull runtime tests exercise the strategy
///     end to end through generated mappers, but none of them reaches THIS surface — round-21 T7 measured
///     <c>TryEnterNode</c> as NoCoverage in the runtime mutation leg (E3-E1 hole 10) — so its documented
///     contract was enforced by nothing. Each test here asserts a sentence of the XML contract:
///     the non-SetNull no-op direction, the enter/re-enter/exit protocol, and the reference-identity
///     (never value-equality) of the guard set.
/// </summary>
public sealed class DwarfRefContextOnStackGuardTests
{
    /// <summary>
    ///     The contract's no-op clause: outside SetNull mode the guard set is not allocated and
    ///     <c>TryEnterNode</c> must return <c>true</c> every time — including for a reference it has
    ///     already seen — so it never changes behaviour in None or Preserve mode.
    /// </summary>
    [Fact]
    public void Outside_SetNull_mode_TryEnterNode_is_a_no_op_that_always_returns_true()
    {
        var node = new object();

        var none = new DwarfRefContext(10);
        Assert.True(none.TryEnterNode(node));
        Assert.True(none.TryEnterNode(node), "None mode has no on-stack guard: re-entering the same "
            + "reference must still report a fresh enter, never a cycle.");

        var preserve = new DwarfRefContext(10, preserve: true);
        Assert.True(preserve.TryEnterNode(node));
        Assert.True(preserve.TryEnterNode(node), "Preserve mode reconstructs cycles via the identity map; "
            + "the on-stack guard must stay inert there too.");
    }

    /// <summary>
    ///     The SetNull protocol, one transition at a time: a fresh node enters (<c>true</c>), the same node
    ///     re-entering while still on the stack is the back-edge (<c>false</c>), a DIFFERENT node is not
    ///     affected, and <c>ExitNode</c> releases the reference so a later visit is a fresh enter again
    ///     (the diamond-vs-cycle distinction the guard's stack-scoping exists for).
    /// </summary>
    [Fact]
    public void In_SetNull_mode_TryEnterNode_detects_reentry_and_ExitNode_releases_the_node()
    {
        var ctx = new DwarfRefContext(10, setNull: true);
        var parent = new object();
        var sibling = new object();

        Assert.True(ctx.TryEnterNode(parent));
        Assert.False(ctx.TryEnterNode(parent), "The same reference re-entering while on the stack is a "
            + "re-entrant back-edge and must report the cycle.");
        Assert.True(ctx.TryEnterNode(sibling), "A distinct node must be unaffected by another node "
            + "being on the stack.");

        ctx.ExitNode(parent);
        Assert.True(ctx.TryEnterNode(parent), "After ExitNode the reference has left the stack: a shared "
            + "but acyclic node (a diamond) is mapped again, not nulled — only true ancestor cycles break.");
    }

    /// <summary>
    ///     The comparer clause, marked CRITICAL in the source: the guard set is keyed by REFERENCE identity,
    ///     so two distinct-but-value-equal records are different nodes and must not collide on the stack.
    ///     With the default comparer the second record would read as a cycle and be silently nulled.
    /// </summary>
    [Fact]
    public void Value_equal_records_are_distinct_nodes_on_the_stack()
    {
        var ctx = new DwarfRefContext(10, setNull: true);
        var first = new PointRecord(1, 2);
        var second = new PointRecord(1, 2);
        Assert.Equal(first, second); // value-equal by record semantics …

        Assert.True(ctx.TryEnterNode(first));
        Assert.True(ctx.TryEnterNode(second), "… but distinct references: the guard must use reference "
            + "identity, or value-equal records collapse into a phantom cycle.");
    }

    private sealed record PointRecord(int X, int Y);
}
