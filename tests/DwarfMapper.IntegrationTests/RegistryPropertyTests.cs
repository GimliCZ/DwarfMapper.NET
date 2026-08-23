// SPDX-License-Identifier: GPL-2.0-only

using CsCheck;
using DwarfMapper.TestInfrastructure;

namespace DwarfMapper.IntegrationTests
{
    /// <summary>
    ///     CsCheck properties over the ambient registry's core contracts: register/lookup round-trip, duplicate
    ///     handling (first-wins, marked), and key identity (both key components matter, and the create/update
    ///     tables never alias). The deferred third bullet of round-21 T4, landed with T7.
    ///     <para>
    ///         The registry is a process-wide static with — deliberately — no reset hook, and CsCheck runs
    ///         iterations on multiple threads. Both constraints shape every property here the same way the E4
    ///         audit describes (<c>AssemblyInfo.cs</c>): each property owns a POOL of closed generic types
    ///         declared below and asserts strictly per-key, so iterations are order- and thread-independent and
    ///         the class needs no serial collection. Delegates are derived from the KEY, never the iteration, so
    ///         whichever concurrent iteration wins a <c>TryAdd</c> the observable behaviour is identical; the
    ///         one-time baseline registrations that must deterministically come FIRST happen in the static
    ///         constructor, before any sampled iteration can add a poison duplicate. No property ever asserts
    ///         <c>IsAmbiguous == false</c> on a key it can touch twice — a second <c>Register</c> marks the key
    ///         even when the delegate is behaviourally identical.
    ///     </para>
    /// </summary>
    public sealed class RegistryPropertyTests
    {
        // CsCheck iteration count: fast 200 (the value these properties have always run with), deep ×10 —
        // see DeepPopulation.RegistryPropertyIters.
        private static readonly int Iters = DeepTier.Count(DeepPopulation.RegistryPropertyIters);

        /// <summary>Round-trip pool. Registered lazily by the property itself; delegates live here, once.</summary>
        private static readonly (PoolEntry Entry, Func<object, object> Map)[] RoundTripPool =
        [
            (new PoolEntry(typeof(PSrc<R1>), typeof(PDst<R1>), () => new PSrc<R1>(), 1), _ => new PDst<R1>
            {
                Marker = 1
            }),
            (new PoolEntry(typeof(PSrc<R2>), typeof(PDst<R2>), () => new PSrc<R2>(), 2), _ => new PDst<R2>
            {
                Marker = 2
            }),
            (new PoolEntry(typeof(PSrc<R3>), typeof(PDst<R3>), () => new PSrc<R3>(), 3), _ => new PDst<R3>
            {
                Marker = 3
            }),
            (new PoolEntry(typeof(PSrc<R4>), typeof(PDst<R4>), () => new PSrc<R4>(), 4), _ => new PDst<R4>
            {
                Marker = 4
            })
        ];

        /// <summary>Duplicate pool: baseline registered in the static ctor so "first" is deterministic.</summary>
        private static readonly PoolEntry[] DuplicatePool =
        [
            new(typeof(PSrc<D1>), typeof(PDst<D1>), () => new PSrc<D1>(), 11),
            new(typeof(PSrc<D2>), typeof(PDst<D2>), () => new PSrc<D2>(), 12),
            new(typeof(PSrc<D3>), typeof(PDst<D3>), () => new PSrc<D3>(), 13),
            new(typeof(PSrc<D4>), typeof(PDst<D4>), () => new PSrc<D4>(), 14)
        ];

        /// <summary>Key-identity pool: registered src→dst ONLY, in the static ctor.</summary>
        private static readonly PoolEntry[] IdentityPool =
        [
            new(typeof(PSrc<K1>), typeof(PDst<K1>), () => new PSrc<K1>(), 21),
            new(typeof(PSrc<K2>), typeof(PDst<K2>), () => new PSrc<K2>(), 22),
            new(typeof(PSrc<K3>), typeof(PDst<K3>), () => new PSrc<K3>(), 23),
            new(typeof(PSrc<K4>), typeof(PDst<K4>), () => new PSrc<K4>(), 24)
        ];

        static RegistryPropertyTests()
        {
            // Baselines that the properties rely on being FIRST. A static ctor runs exactly once, before any
            // iteration; registering here is what makes "first wins" assertable under parallel sampling.
            foreach (var entry in DuplicatePool)
            {
                var marker = entry.Marker;
                DwarfMapperRegistry.Register(entry.Source,
                    entry.Destination,
                    _ => MakeDst(entry.Destination, marker));
            }

            foreach (var entry in IdentityPool)
            {
                var marker = entry.Marker;
                DwarfMapperRegistry.Register(entry.Source,
                    entry.Destination,
                    _ => MakeDst(entry.Destination, marker));
            }
        }

        /// <summary>Compile-time construction per closed type — no Activator, honoring the no-reflection stance.</summary>
        private static object MakeDst(Type destination, int marker)
        {
            if (destination == typeof(PDst<D1>))
            {
                return new PDst<D1>
                {
                    Marker = marker
                };
            }

            if (destination == typeof(PDst<D2>))
            {
                return new PDst<D2>
                {
                    Marker = marker
                };
            }

            if (destination == typeof(PDst<D3>))
            {
                return new PDst<D3>
                {
                    Marker = marker
                };
            }

            if (destination == typeof(PDst<D4>))
            {
                return new PDst<D4>
                {
                    Marker = marker
                };
            }

            if (destination == typeof(PDst<K1>))
            {
                return new PDst<K1>
                {
                    Marker = marker
                };
            }

            if (destination == typeof(PDst<K2>))
            {
                return new PDst<K2>
                {
                    Marker = marker
                };
            }

            if (destination == typeof(PDst<K3>))
            {
                return new PDst<K3>
                {
                    Marker = marker
                };
            }

            if (destination == typeof(PDst<K4>))
            {
                return new PDst<K4>
                {
                    Marker = marker
                };
            }

            throw new InvalidOperationException($"No factory for {destination}.");
        }

        private static int MarkerOf(object dst)
        {
            return dst switch
            {
                PDst<R1> d => d.Marker, PDst<R2> d => d.Marker, PDst<R3> d => d.Marker, PDst<R4> d => d.Marker,
                PDst<D1> d => d.Marker, PDst<D2> d => d.Marker, PDst<D3> d => d.Marker, PDst<D4> d => d.Marker,
                PDst<K1> d => d.Marker, PDst<K2> d => d.Marker, PDst<K3> d => d.Marker, PDst<K4> d => d.Marker,
                _ => throw new InvalidOperationException($"Unexpected destination {dst}.")
            };
        }

        /// <summary>
        ///     Register → IsProvided / TryGet / Map all agree, per key, and hand back the registered behaviour.
        ///     Registration is deliberately re-run inside the iteration: repeated registration must never
        ///     UNREGISTER or swap a key (first wins), so the round-trip holds on every iteration, not just the
        ///     first.
        /// </summary>
        [Fact]
        public void Registering_a_pair_makes_it_resolvable_and_the_delegate_round_trips()
        {
            Gen.Int[0, RoundTripPool.Length - 1].Sample(i =>
                {
                    var ((source, destination, instance, marker), map) = RoundTripPool[i];

                    DwarfMapperRegistry.Register(source, destination, map);

                    Assert.True(DwarfMapperRegistry.IsProvided(source, destination));

                    Assert.True(DwarfMapperRegistry.TryGet(source, destination, out var got));
                    Assert.Equal(marker, MarkerOf(got!(instance())));

                    Assert.Equal(marker, MarkerOf(DwarfMapperRegistry.Map(instance(), destination)));
                },
                iter: Iters);
        }

        /// <summary>
        ///     Duplicate handling per the stated invariant: a second DISTINCT provider for a pair never wins and
        ///     never unregisters — the first delegate keeps serving — and the pair is MARKED ambiguous rather
        ///     than throwing at load. The poison registration happens inside the iteration, so the duplicate path
        ///     is exercised under the same parallelism module initializers race under.
        /// </summary>
        [Fact]
        public void A_duplicate_registration_is_first_wins_and_marked()
        {
            Gen.Int[0, DuplicatePool.Length - 1].Sample(i =>
                {
                    var (source, destination, instance, marker) = DuplicatePool[i];

                    DwarfMapperRegistry.Register(source, destination, _ => MakeDst(destination, -1));

                    Assert.True(DwarfMapperRegistry.IsAmbiguous(source, destination),
                        "a second distinct provider must be marked, not silently dropped nor thrown at load");
                    Assert.True(DwarfMapperRegistry.IsProvided(source, destination),
                        "marking a duplicate must not unregister the pair");
                    Assert.Equal(marker, MarkerOf(DwarfMapperRegistry.Map(instance(), destination)));
                },
                iter: Iters);
        }

        /// <summary>
        ///     Key identity: a registration is keyed on the ORDERED (source, destination) pair — sharing one
        ///     component resolves nothing — and it never leaks into the update table's key space.
        /// </summary>
        [Fact]
        public void A_key_matches_only_its_exact_ordered_pair_and_only_its_own_table()
        {
            Gen.Int[0, IdentityPool.Length - 1].Select(Gen.Int[0, IdentityPool.Length - 1]).Sample(t =>
                {
                    var (i, j) = t;
                    var (source, destination, _, _) = IdentityPool[i];
                    var other = IdentityPool[j];

                    Assert.True(DwarfMapperRegistry.IsProvided(source, destination));

                    // Reversed and reflexive keys share a component with a registered key; none may resolve.
                    Assert.False(DwarfMapperRegistry.IsProvided(destination, source));
                    Assert.False(DwarfMapperRegistry.IsProvided(source, source));
                    Assert.False(DwarfMapperRegistry.IsProvided(destination, destination));
                    Assert.False(DwarfMapperRegistry.TryGet(destination, source, out _));

                    // Cross-entry: both components must match the SAME registration.
                    if (i != j)
                    {
                        Assert.False(DwarfMapperRegistry.IsProvided(source, other.Destination));
                    }

                    // A create registration must never surface from the update table's key space.
                    Assert.False(DwarfMapperRegistry.IsUpdateProvided(source, destination));
                },
                iter: Iters);
        }

        // Distinct closed generic types give distinct registry keys without declaring dozens of classes — the
        // torture suite's convention. The type parameter is carried on a property so it is genuinely part of
        // each closed type.
        private sealed class PSrc<T>
        {
            public T? Tag { get; set; }
        }

        private sealed class PDst<T>
        {
            public int Marker { get; set; }

            public T? Tag { get; set; }
        }

        private sealed class R1;

        private sealed class R2;

        private sealed class R3;

        private sealed class R4;

        private sealed class D1;

        private sealed class D2;

        private sealed class D3;

        private sealed class D4;

        private sealed class K1;

        private sealed class K2;

        private sealed class K3;

        private sealed class K4;

        private readonly record struct PoolEntry(Type Source, Type Destination, Func<object> Instance, int Marker);
    }
}
