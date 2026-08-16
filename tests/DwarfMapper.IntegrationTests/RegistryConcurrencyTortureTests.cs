// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Concurrent;

namespace DwarfMapper.IntegrationTests;

/// <summary>
///     The registry is populated by <c>[ModuleInitializer]</c> code, which the runtime may execute on
///     different threads as assemblies load, while application threads are already calling
///     <see cref="DwarfMapperRegistry.Map" />. So registration racing registration, and registration racing
///     resolution, are the NORMAL conditions for this type — not exotic ones. Its correctness rests entirely
///     on two <see cref="ConcurrentDictionary{TKey,TValue}" /> fields and on <c>TryAdd</c> being the thing
///     that decides who wins; a later refactor to "check then add", or to a plain dictionary with a lock
///     taken in the wrong place, would still pass every single-threaded test in this suite.
///     <para>
///         These tests use a <see cref="Barrier" /> so every thread starts pushing at the same instant
///         rather than trickling in, and they deliberately do NOT reset global state: each test registers its
///         own distinct closed generic types, which is what keeps them isolated from the module initializers
///         and from each other. They are pinned to a non-parallel collection anyway, because contention
///         measured while the rest of the suite saturates the CPU is contention measured against noise.
///     </para>
///     <para>
///         Caveat on power: a torture test can demonstrate a race but cannot prove the absence of one, and a
///         green run on a machine with few cores is weak evidence. Its power against the regression it
///         targets WAS measured, on 16 threads:
///         <list type="bullet">
///             <item>replacing <c>TryAdd</c> with last-writer-wins → caught in <b>60 of 60</b> rounds;</item>
///             <item>replacing it with check-then-act (a far narrower window) → caught in <b>22–26 of 60</b>
///             rounds, failing on every one of three consecutive runs.</item>
///         </list>
///         That number is what the round count buys. An earlier version of this test had NO power at all: the
///         registered lambda closed only over an outer local, so the compiler hoisted it and every thread
///         handed <c>Register</c> the same delegate instance — "exactly one winner" was then true by
///         construction and both broken registries passed. See the capture note in the first test.
///     </para>
///     <para>
///         The UPDATE-table mirrors below were measured the same way, and needed four times the rounds to
///         reach comparable power. The create table has <c>TryGet</c>, so its observer reads the winning
///         delegate directly; the update table deliberately has no such accessor, so its observer must go
///         through <c>Update</c>, which invokes the delegate — a far more expensive poll, and therefore
///         fewer samples inside the same contested window. At 240 rounds:
///         <list type="bullet">
///             <item>last-writer-wins → caught in <b>58 of 60</b> rounds even before the round count was
///             raised;</item>
///             <item>check-then-act → <b>23, 54 and 58 of 240</b> on three consecutive runs. At 60 rounds the
///             same mutant scored 1, 6 and 7 — it still failed every run, but a margin of one round is a
///             coin-flip away from greening a broken registry, which is why the count is 240 and not 60.</item>
///         </list>
///         The first version of that observer was weaker still, at 2 of 60: it polled inside a
///         <c>try</c>/<c>catch</c>, so every poll before the first registration landed threw
///         <c>DwarfMapMissingException</c> and the observer spent the contested window unwinding stacks.
///     </para>
/// </summary>
[Collection("registry-torture")]
public sealed class RegistryConcurrencyTortureTests
{
    private static readonly int Threads = Math.Max(8, Environment.ProcessorCount * 2);

    // Distinct closed generic types give distinct registry keys without declaring dozens of classes.
    // The type parameter is carried on a property so it is genuinely part of each closed type.
    private sealed class Src<T> { public T? Tag { get; set; } }

    private sealed class Dst<T> { public T? Tag { get; set; } }

    private sealed class K1;
    private sealed class K2;
    private sealed class K3;
    private sealed class K4;
    private sealed class K5;
    private sealed class K6;
    private sealed class K7;
    private sealed class K8;

    private static readonly Type[] Tags = { typeof(K1), typeof(K2), typeof(K3), typeof(K4), typeof(K5), typeof(K6), typeof(K7), typeof(K8) };

    private static Type SrcOf(Type tag) => typeof(Src<>).MakeGenericType(tag);
    private static Type DstOf(Type tag) => typeof(Dst<>).MakeGenericType(tag);

    /// <summary>Runs <paramref name="body" /> on <see cref="Threads" /> threads released simultaneously.</summary>
    private static List<Exception> RunTogether(Action<int> body)
    {
        var failures = new ConcurrentBag<Exception>();
        using var barrier = new Barrier(Threads);
        var workers = new Thread[Threads];

        for (var i = 0; i < Threads; i++)
        {
            var index = i;
            workers[i] = new Thread(() =>
            {
                try
                {
                    barrier.SignalAndWait();
                    body(index);
                }
#pragma warning disable CA1031 // Do not catch general exception types
                // Deliberate: an unhandled exception on a raw Thread tears down the test PROCESS, so the
                // harness would report "the run crashed" instead of "the registry raced". Catching
                // everything and asserting the bag is empty is what turns a crash into a readable failure.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    failures.Add(ex);
                }
            })
            { IsBackground = true };
        }

        foreach (var w in workers) w.Start();
        foreach (var w in workers) Assert.True(w.Join(TimeSpan.FromSeconds(30)), "a torture thread did not finish");

        return failures.ToList();
    }

    // ── One key, many racing registrations ───────────────────────────────────────────────────────────
    // Every thread offers a DIFFERENT delegate for the same pair. Exactly one must win, and it must keep
    // winning: a "last writer wins" implementation would let the resolved delegate change under a caller
    // that already saw a different answer.
    [Fact]
    public void Racing_registrations_of_one_key_leave_exactly_one_winner()
    {
        // The load-bearing observation happens DURING the storm, not after it. Checking stability once every
        // thread has joined proves nothing: a last-writer-wins implementation also settles on one delegate
        // and also ends up marked ambiguous, so an after-the-fact check passes on a broken registry —
        // measured, not assumed. What separates the two is whether a reader can ever see the answer CHANGE.
        //
        // One storm is also not enough: a single key is contested for microseconds and an observer thread
        // may not be scheduled inside that window at all. So the experiment is repeated over many FRESH
        // keys, and the violation counter accumulates across all of them.
        const int rounds = 60;
        var replacementsSeen = 0;
        var allFailures = new List<Exception>();

        for (var round = 0; round < rounds; round++)
        {
            var src = FreshType(round);
            var dst = DstOf(typeof(K1));

            var observed = new ConcurrentDictionary<Func<object, object>, byte>();
            var stop = false;

            var observer = new Thread(() =>
            {
                while (!Volatile.Read(ref stop))
                    if (DwarfMapperRegistry.TryGet(src, dst, out var current) && current is not null)
                        observed.TryAdd(current, 0);
            })
            { IsBackground = true };

            observer.Start();
            // The lambda MUST capture something per-thread. A lambda that closes only over `dst` (an outer
            // local) is hoisted into the enclosing display class and created once, so every thread would
            // hand Register the very same delegate instance and "exactly one winner" would be trivially
            // true no matter how broken the registry is. Capturing `threadIndex` forces a distinct closure
            // per call — this test had no power at all until that was fixed.
            allFailures.AddRange(RunTogether(threadIndex =>
            {
                var mine = threadIndex;
                DwarfMapperRegistry.Register(src, dst, _ =>
                {
                    _ = mine;
                    return Activator.CreateInstance(dst)!;
                });
            }));
            Volatile.Write(ref stop, true);
            Assert.True(observer.Join(TimeSpan.FromSeconds(30)), "the observer thread did not finish");

            Assert.True(DwarfMapperRegistry.IsProvided(src, dst), $"round {round}: the key was lost");
            if (observed.Count > 1) replacementsSeen++;

            // The winner stays put after the storm settles.
            Assert.True(DwarfMapperRegistry.TryGet(src, dst, out var first));
            Assert.True(DwarfMapperRegistry.TryGet(src, dst, out var again));
            Assert.Same(first, again);

            // With Threads > 1 offering distinct delegates, the losers must be recorded, not swallowed.
            Assert.True(DwarfMapperRegistry.IsAmbiguous(src, dst), $"round {round}: ambiguity was not recorded");
        }

        Assert.Empty(allFailures);
        Assert.True(replacementsSeen == 0,
            $"a reader saw the delegate for a key REPLACED in {replacementsSeen} of {rounds} rounds — "
            + "a registration became visible and was then overwritten, so TryAdd is no longer what decides "
            + "the winner.");
    }

    // Distinct closed types on demand: List&lt;List&lt;…&lt;K1&gt;&gt;&gt; nested `depth` times is a different Type for every
    // depth, which gives each round its own uncontested registry key without declaring 60 classes.
    private static Type FreshType(int depth)
    {
        var t = typeof(K1);
        for (var i = 0; i <= depth; i++) t = typeof(List<>).MakeGenericType(t);
        return typeof(Src<>).MakeGenericType(t);
    }

    // ── Many keys, all racing ────────────────────────────────────────────────────────────────────────
    // Distinct keys must not interfere: every one lands, none is reported ambiguous, and each resolves to
    // its own delegate rather than to a neighbour's.
    [Fact]
    public void Racing_registrations_of_distinct_keys_all_land_unambiguously()
    {
        var tags = Tags.Skip(1).ToArray(); // K1 belongs to the test above

        var failures = RunTogether(_ =>
        {
            foreach (var tag in tags)
            {
                var s = SrcOf(tag);
                var d = DstOf(tag);
                // Same delegate instance per key from every thread, so a losing TryAdd is a genuine
                // duplicate rather than a competing provider.
                DwarfMapperRegistry.Register(s, d, DelegateFor(d));
            }
        });

        Assert.Empty(failures);

        foreach (var tag in tags)
        {
            var s = SrcOf(tag);
            var d = DstOf(tag);
            Assert.True(DwarfMapperRegistry.IsProvided(s, d), $"{s.Name} -> {d.Name} was lost");

            var mapped = DwarfMapperRegistry.Map(Activator.CreateInstance(s)!, d);
            Assert.Equal(d, mapped.GetType());
        }
    }

    private static readonly ConcurrentDictionary<Type, Func<object, object>> Delegates = new();

    private static Func<object, object> DelegateFor(Type dst) =>
        Delegates.GetOrAdd(dst, d => _ => Activator.CreateInstance(d)!);

    // ── Readers racing writers ───────────────────────────────────────────────────────────────────────
    // Half the threads resolve an already-registered pair in a tight loop while the other half register
    // new pairs. Resolution must never observe a half-built registry: no exception, and never the wrong
    // destination type.
    [Fact]
    public void Resolution_is_unaffected_by_concurrent_registration()
    {
        var stableSrc = SrcOf(typeof(K2));
        var stableDst = DstOf(typeof(K2));
        DwarfMapperRegistry.Register(stableSrc, stableDst, DelegateFor(stableDst));

        var churnTags = new[] { typeof(int), typeof(long), typeof(short), typeof(byte), typeof(uint), typeof(ulong) };

        var failures = RunTogether(i =>
        {
            if (i % 2 == 0)
            {
                for (var n = 0; n < 500; n++)
                {
                    var mapped = DwarfMapperRegistry.Map(Activator.CreateInstance(stableSrc)!, stableDst);
                    if (mapped.GetType() != stableDst)
                        throw new InvalidOperationException($"resolved to {mapped.GetType()} instead of {stableDst}");
                }
            }
            else
            {
                foreach (var tag in churnTags)
                    DwarfMapperRegistry.Register(SrcOf(tag), DstOf(tag), DelegateFor(DstOf(tag)));
            }
        });

        Assert.Empty(failures);
    }

    // ── Snapshotting while writing ───────────────────────────────────────────────────────────────────
    // `Provided` pre-sizes a list from Count and then enumerates Keys — two reads of a moving structure.
    // That is safe on ConcurrentDictionary and would NOT be safe on a plain Dictionary; this is the guard
    // that notices if the field type ever changes.
    [Fact]
    public void Enumerating_Provided_while_registering_does_not_throw()
    {
        var churnTags = new[] { typeof(float), typeof(double), typeof(decimal), typeof(char), typeof(bool), typeof(string) };

        var failures = RunTogether(i =>
        {
            if (i % 2 == 0)
            {
                for (var n = 0; n < 200; n++)
                {
                    var count = 0;
                    foreach (var (s, d) in DwarfMapperRegistry.Provided)
                    {
                        Assert.NotNull(s);
                        Assert.NotNull(d);
                        count++;
                    }

                    Assert.True(count >= 0);
                }
            }
            else
            {
                foreach (var tag in churnTags)
                    DwarfMapperRegistry.Register(SrcOf(tag), DstOf(tag), DelegateFor(DstOf(tag)));
            }
        });

        Assert.Empty(failures);
    }

    // ── The idempotency wording, pinned ──────────────────────────────────────────────────────────────
    // Register's doc says a second DISTINCT provider is recorded as ambiguous. The implementation marks
    // ambiguity whenever TryAdd fails, which includes re-registering the SAME delegate — so a module
    // initializer that runs twice (or a test that registers defensively) marks the pair ambiguous even
    // though there is only one provider. Pinned as observed rather than as documented: whichever way the
    // maintainer resolves the wording, this test is what makes the choice visible.
    [Fact]
    public void Re_registering_the_identical_delegate_still_marks_the_pair_ambiguous()
    {
        var src = SrcOf(typeof(K3));
        var dst = DstOf(typeof(K3));
        var one = DelegateFor(dst);

        DwarfMapperRegistry.Register(src, dst, one);
        Assert.False(DwarfMapperRegistry.IsAmbiguous(src, dst));

        DwarfMapperRegistry.Register(src, dst, one);

        Assert.True(DwarfMapperRegistry.IsAmbiguous(src, dst),
            "Observed behaviour: ambiguity is keyed on TryAdd failing, not on the provider differing.");
    }

    // ══ The update table ═════════════════════════════════════════════════════════════════════════════
    //
    // Everything above tortures the CREATE table. `RegisterUpdate` writes to a second ConcurrentDictionary
    // on the same shared static, under the same conditions — module initializers registering on whatever
    // thread the runtime loads an assembly on — and had no torture coverage at all, nor any contract test:
    // it was referenced exactly twice in the repository, its declaration and the generator emitting calls to
    // it, and ran only implicitly. What that hid is pinned in RegistryUpdateContractTests: a duplicate was
    // marked in the CREATE table's ambiguity set, the only one that existed.
    //
    // Four of the five invariants above have an update-side twin and are mirrored below. One does NOT, and
    // is stated here rather than silently absent, because the next reader will otherwise assume a parity
    // that does not exist — as is a sixth asymmetry that is NOT one of the five, but which someone
    // mirroring this file mechanically would reach for:
    //
    //   * `Enumerating_Provided_while_registering_does_not_throw` has no twin. It guards `Provided`, which
    //     pre-sizes a list from Count and then enumerates Keys — two reads of a moving structure. The update
    //     table exposes no such enumerator (validation calls `IsProvided`, never `Provided`), so there is no
    //     two-read snapshot to race. Adding one purely to torture it would be shipping surface to satisfy a
    //     test. If an update-table enumerator is ever added, this is the test it needs.
    //   * Base/interface resolution has no twin, and must not acquire one. It is not one of the five
    //     invariants above either — there is no create-side test of it here to mirror — but `Map` does walk
    //     base types and then interfaces, so it is the obvious thing to reach for. `Update` resolves by the
    //     DECLARED types only, deliberately: writing into an instance the caller already holds must not
    //     dispatch on a more-derived runtime type. That asymmetry is a safety property documented on
    //     `Update` itself, so a test asserting update-side hierarchy resolution would assert a regression.
    //
    // One mechanical difference in HOW the mirrors observe. The create table has `TryGet`, so its one-key
    // race can watch the winning delegate directly. The update table has no `TryGetUpdate`, and adding one
    // for a test's benefit is the same mistake as adding an enumerator, so the update mirrors observe
    // through `Update`'s side effect: each thread's delegate stamps its own index onto the destination, and
    // the set of stamps a reader ever sees is the set of delegates that were ever live.

    private sealed class U1;

    private sealed class U2;

    private sealed class U3;

    private sealed class U4;

    private static readonly Type[] UpdateTags = { typeof(U2), typeof(U3), typeof(U4) };

    /// <summary>
    ///     A non-generic destination, so a registered delegate can cast to it directly. Only the SOURCE type
    ///     needs to be fresh per round — the key is the pair — which is the same shape as the create-side
    ///     race, where <c>dst</c> is held at <c>Dst&lt;K1&gt;</c> throughout.
    /// </summary>
    private sealed class UpdateBox
    {
        public int Winner { get; set; }
    }

    /// <summary>Distinct closed types on demand, over <see cref="U1" /> so they cannot collide with <see cref="FreshType" />.</summary>
    private static Type FreshUpdateType(int depth)
    {
        var t = typeof(U1);
        for (var i = 0; i <= depth; i++) t = typeof(List<>).MakeGenericType(t);
        return typeof(Src<>).MakeGenericType(t);
    }

    /// <summary>
    ///     Reads the currently-winning delegate's stamp. Only valid once the pair is registered — the
    ///     observer below gates on <c>IsUpdateProvided</c> rather than catching the miss.
    /// </summary>
    private static int CurrentStamp(Type src, object srcInstance, UpdateBox box)
    {
        DwarfMapperRegistry.Update(srcInstance, box, src, typeof(UpdateBox));
        return box.Winner;
    }

    // ── One key, many racing update registrations ────────────────────────────────────────────────────
    // The twin of Racing_registrations_of_one_key_leave_exactly_one_winner, and it carries the same
    // load-bearing observation: the check that separates first-wins from last-writer-wins has to happen
    // DURING the storm. Both implementations settle on one delegate and both end up marked; what differs is
    // whether a reader can ever see the answer CHANGE.
    [Fact]
    public void Racing_update_registrations_of_one_key_leave_exactly_one_winner()
    {
        // 240, not the create side's 60: the update observer must poll through Update (which invokes the
        // delegate) rather than TryGet, so it samples the contested window far less densely. See the power
        // figures in the class remarks — at 60 rounds the check-then-act mutant scored as low as 1.
        const int rounds = 240;
        var replacementsSeen = 0;
        var allFailures = new List<Exception>();

        for (var round = 0; round < rounds; round++)
        {
            var src = FreshUpdateType(round);
            var srcInstance = Activator.CreateInstance(src)!;

            var observed = new ConcurrentDictionary<int, byte>();
            var stop = false;

            // Throughput here IS the test's power, and the naive observer had almost none. Polling with a
            // try/catch around Update meant every poll before the first registration landed threw
            // DwarfMapMissingException — microseconds each — so the observer spent the whole contested
            // window unwinding stacks instead of sampling. Gating on IsUpdateProvided and reusing one box
            // took the check-then-act detection rate from 2/60 rounds to the figures in the class remarks.
            var observerBox = new UpdateBox();
            var observer = new Thread(() =>
            {
                while (!Volatile.Read(ref stop))
                    if (DwarfMapperRegistry.IsUpdateProvided(src, typeof(UpdateBox)))
                        observed.TryAdd(CurrentStamp(src, srcInstance, observerBox), 0);
            })
            { IsBackground = true };

            observer.Start();
            // Same capture requirement as the create-side race: the lambda MUST close over something
            // per-thread. A delegate hoisted into the enclosing display class would be the SAME instance
            // from every thread, and "exactly one winner" would then be true by construction on a registry
            // of any quality. `mine` is what gives this test its power.
            allFailures.AddRange(RunTogether(threadIndex =>
            {
                var mine = threadIndex + 1; // 1-based: 0 is the unwritten Winner and would be unobservable
                DwarfMapperRegistry.RegisterUpdate(src, typeof(UpdateBox), (_, d) => ((UpdateBox)d).Winner = mine);
            }));
            Volatile.Write(ref stop, true);
            Assert.True(observer.Join(TimeSpan.FromSeconds(30)), "the observer thread did not finish");

            Assert.True(DwarfMapperRegistry.IsUpdateProvided(src, typeof(UpdateBox)), $"round {round}: the key was lost");
            if (observed.Count > 1) replacementsSeen++;

            // The winner stays put after the storm settles.
            var settledBox = new UpdateBox();
            var settled = CurrentStamp(src, srcInstance, settledBox);
            Assert.NotEqual(0, settled);
            Assert.Equal(settled, CurrentStamp(src, srcInstance, settledBox));

            Assert.True(DwarfMapperRegistry.IsUpdateAmbiguous(src, typeof(UpdateBox)),
                $"round {round}: ambiguity was not recorded on the update table");

            // And it was recorded on the UPDATE table only. This is the contamination guard from
            // RegistryUpdateContractTests, re-asserted under contention: a racing implementation that fell
            // back to the create table's set would satisfy the line above and still be wrong.
            Assert.False(DwarfMapperRegistry.IsAmbiguous(src, typeof(UpdateBox)),
                $"round {round}: an update-table duplicate was recorded against the CREATE table, which has "
                + "no map for this pair at all.");
        }

        Assert.Empty(allFailures);
        Assert.True(replacementsSeen == 0,
            $"a reader saw the update delegate for a key REPLACED in {replacementsSeen} of {rounds} rounds — "
            + "a registration became visible and was then overwritten, so TryAdd is no longer what decides "
            + "the winner on the update table.");
    }

    // ── Many update keys, all racing ─────────────────────────────────────────────────────────────────
    // The twin of Racing_registrations_of_distinct_keys_all_land_unambiguously.
    [Fact]
    public void Racing_update_registrations_of_distinct_keys_all_land_unambiguously()
    {
        var failures = RunTogether(_ =>
        {
            foreach (var tag in UpdateTags)
                // Same delegate instance per key from every thread, so a losing TryAdd is a genuine
                // duplicate rather than a competing provider.
                DwarfMapperRegistry.RegisterUpdate(SrcOf(tag), typeof(UpdateBox), StampFor(tag));
        });

        Assert.Empty(failures);

        foreach (var tag in UpdateTags)
        {
            var s = SrcOf(tag);
            Assert.True(DwarfMapperRegistry.IsUpdateProvided(s, typeof(UpdateBox)), $"{s.Name} update map was lost");

            // Each key resolves to its OWN delegate rather than to a neighbour's.
            var box = new UpdateBox();
            DwarfMapperRegistry.Update(Activator.CreateInstance(s)!, box, s, typeof(UpdateBox));
            Assert.Equal(Array.IndexOf(UpdateTags, tag) + 1, box.Winner);
        }
    }

    private static readonly ConcurrentDictionary<Type, Action<object, object>> Stamps = new();

    private static Action<object, object> StampFor(Type tag) =>
        Stamps.GetOrAdd(tag, t =>
        {
            var stamp = Array.IndexOf(UpdateTags, t) + 1;
            return (_, d) => ((UpdateBox)d).Winner = stamp;
        });

    // ── Update resolution racing update registration ─────────────────────────────────────────────────
    // The twin of Resolution_is_unaffected_by_concurrent_registration. Half the threads apply an
    // already-registered update map in a tight loop while the other half register new ones; resolution must
    // never observe a half-built table — no exception, and never a neighbour's delegate.
    [Fact]
    public void Update_resolution_is_unaffected_by_concurrent_registration()
    {
        var stableSrc = SrcOf(typeof(U1));
        DwarfMapperRegistry.RegisterUpdate(stableSrc, typeof(UpdateBox), (_, d) => ((UpdateBox)d).Winner = 99);

        var churnTags = new[] { typeof(int), typeof(long), typeof(short), typeof(byte), typeof(uint), typeof(ulong) };

        var failures = RunTogether(i =>
        {
            if (i % 2 == 0)
            {
                var source = Activator.CreateInstance(stableSrc)!;
                for (var n = 0; n < 500; n++)
                {
                    var box = new UpdateBox();
                    DwarfMapperRegistry.Update(source, box, stableSrc, typeof(UpdateBox));
                    if (box.Winner != 99)
                        throw new InvalidOperationException($"resolved to a delegate stamping {box.Winner} instead of 99");
                }
            }
            else
            {
                foreach (var tag in churnTags)
                    DwarfMapperRegistry.RegisterUpdate(SrcOf(tag), typeof(UpdateBox), StampFor(tag));
            }
        });

        Assert.Empty(failures);
    }

    // ── The idempotency wording, pinned on the update table too ──────────────────────────────────────
    // The twin of Re_registering_the_identical_delegate_still_marks_the_pair_ambiguous. Same observed
    // behaviour, same reason: ambiguity is keyed on TryAdd failing, not on the provider differing. Pinned on
    // both tables so that whichever way the maintainer resolves the wording, it gets resolved for both — the
    // update table having quietly drifted from the create table is precisely how this task started.
    [Fact]
    public void Re_registering_the_identical_update_delegate_still_marks_the_pair_ambiguous()
    {
        var one = StampFor(typeof(U2));

        // The assertion is about the TRANSITION from unmarked to marked, so this needs a key no other test
        // in the collection has touched — U2's own pair is registered by the distinct-keys test above, and
        // xUnit gives no ordering guarantee between them.
        var soloSrc = typeof(Src<>).MakeGenericType(typeof(Dictionary<,>).MakeGenericType(typeof(U2), typeof(U3)));
        DwarfMapperRegistry.RegisterUpdate(soloSrc, typeof(UpdateBox), one);
        Assert.False(DwarfMapperRegistry.IsUpdateAmbiguous(soloSrc, typeof(UpdateBox)));

        DwarfMapperRegistry.RegisterUpdate(soloSrc, typeof(UpdateBox), one);

        Assert.True(DwarfMapperRegistry.IsUpdateAmbiguous(soloSrc, typeof(UpdateBox)),
            "Observed behaviour: ambiguity is keyed on TryAdd failing, not on the provider differing.");
        Assert.False(DwarfMapperRegistry.IsAmbiguous(soloSrc, typeof(UpdateBox)),
            "and it is recorded against the update table only.");
    }
}

/// <summary>
///     Contention measured while the rest of the suite saturates the CPU is contention measured against
///     noise, so these run alone.
/// </summary>
[CollectionDefinition("registry-torture", DisableParallelization = true)]
public sealed class RegistryTortureCollection;
