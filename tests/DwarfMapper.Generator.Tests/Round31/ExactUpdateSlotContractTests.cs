// SPDX-License-Identifier: GPL-2.0-only
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     The update-into facade's exact-pair slot, pinned directly. Through the facade a slot that never answers is
    ///     invisible: a miss falls back to <c>DwarfMapperRegistry.Update</c>, which resolves the SAME declared pair, so
    ///     the runtime mutation leg (2026-09-27) left <c>TryGetUpdate</c> gutted and the slot's found/not-found test
    ///     inverted as survivors - the fast path could be silently dead and every behavioural test would still pass.
    ///     The create-side twin does not need this: its fallback dispatches on the runtime type, which a test observes.
    /// </summary>
    public sealed class ExactUpdateSlotContractTests
    {
        private interface ISlotSource;

        private interface ISlotDestination;

        private interface IUnregisteredSource;

        [Fact]
        public void A_registered_update_pair_is_answered_by_the_slot_with_the_registered_delegate()
        {
            Action<object, object> registered = static (_, _) => { };
            DwarfMapperRegistry.RegisterUpdate(typeof(ISlotSource), typeof(ISlotDestination), registered);

            Assert.True(DwarfMapperRegistry.TryGetUpdate(typeof(ISlotSource), typeof(ISlotDestination), out var looked));
            Assert.Same(registered, looked);
            Assert.Same(registered, ExactUpdateSlot<ISlotSource, ISlotDestination>.Get());
            // The second read is the cached one; it must be the same delegate, not a re-resolution of something else.
            Assert.Same(registered, ExactUpdateSlot<ISlotSource, ISlotDestination>.Get());
        }

        [Fact]
        public void An_unregistered_update_pair_is_a_miss_so_the_facade_routes_through_Update()
        {
            Assert.False(DwarfMapperRegistry.TryGetUpdate(typeof(IUnregisteredSource), typeof(ISlotDestination), out var looked));
            Assert.Null(looked);
            Assert.Null(ExactUpdateSlot<IUnregisteredSource, ISlotDestination>.Get());
        }
    }
}
