// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    /// <summary>
    ///     The on-stack guard's contract OUTSIDE SetNull mode. Generated code pairs <c>TryEnterNode</c> with
    ///     <c>ExitNode</c> in a <c>finally</c>; only a SetNull mapper emits that pair, so the no-op half of
    ///     <see cref="DwarfRefContext.ExitNode" /> — the context without a stack — had never run. It must stay a
    ///     no-op rather than throw, because a context built without <c>setNull</c> has nothing to remove from.
    /// </summary>
    public sealed class RefContextStackGuardTests
    {
        [Fact]
        public void ExitNode_without_SetNull_is_a_no_op_and_entering_again_still_succeeds()
        {
            var context = new DwarfRefContext(10);
            var node = new object();

            Assert.True(context.TryEnterNode(node));
            context.ExitNode(node);

            // No stack was allocated, so nothing was tracked: entering the same node again is not a cycle.
            Assert.True(context.TryEnterNode(node));
        }

        [Fact]
        public void ExitNode_with_SetNull_removes_the_node_so_it_can_be_entered_again()
        {
            // The twin, so the no-op above is distinguishable from a stack that simply never records anything.
            var context = new DwarfRefContext(10, setNull: true);
            var node = new object();

            Assert.True(context.TryEnterNode(node));
            Assert.False(context.TryEnterNode(node));
            context.ExitNode(node);
            Assert.True(context.TryEnterNode(node));
        }
    }
}
