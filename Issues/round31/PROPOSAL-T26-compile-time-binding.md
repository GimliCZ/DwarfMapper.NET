# T26 — binding the ambient call site at compile time (and why interceptors are not a straight answer)

Status: **proposal, not a plan.** One load-bearing question is unverified and named below; the recommendation
depends on it.

## The problem, stated without the solution in it

`IDwarfMapper.Map<TSource, TDestination>(source)` knows both types at the call site. The generator sees that
call site. `DWARF061` at the validation root and `DWARF005` at the pair already fail the build when the linkage
is missing. So every part of the question "which map applies here?" is answered before the program runs — and
the shipped code answers it again at run time, with a dictionary probe on a `(Type, Type)` key.

Round 31 made that cost two things rather than one:

- **time** — 8.57 ns of dispatch overhead over a direct call, after T12; 13.05 ns before it;
- **unkillable mutants** — the slot that removed most of the first cost has no observable behaviour, so its
  machinery cannot be killed by any honest test. The runtime leg's 93.13 % is mostly that.

The second cost is the interesting one, because it does not go away by optimising. A cache is a permanent
supply of mutation survivors. The only way to stop paying it is to not need the cache, which means not needing
the lookup.

## What compile-time binding would delete

`ExactPairSlot`, `ExactUpdateSlot`, both mutable statics, the `Volatile` reads and writes, four to five
permanently undetected mutants, and the lookup itself — replaced by a direct call to the generated mapper, which
is what `*_DwarfDirect` already measures at **5.12 ns** against the facade's 13.69 ns.

## Why C# interceptors are not a straight answer

`[InterceptsLocation]` replaces a specific call site's target with a generated method. Applied here it has a
correctness hazard that is easy to miss and would be severe:

**The facade is reached through an interface, and that is the whole point of it.** A team arriving from
AutoMapper injects `IDwarfMapper`. Intercepting a call whose receiver is interface-typed replaces a virtual
dispatch with a static one — so a consumer who registered *their own* `IDwarfMapper` implementation (a decorator
that logs, a test double, a tenant-aware router) would silently stop being called. The interceptor would be
correct about the types and wrong about the receiver.

That is not a detail to handle later. It is the difference between an optimisation and a silent behaviour change
in someone else's dependency-injection graph, and this library's whole position is that it does not do those.

So interception is sound only where the receiver is statically the concrete `DwarfMapperFacade`, which is the
case that needs it least — code holding the concrete facade could call the generated mapper directly.

## The shape that would work

Offer a **non-virtual, statically-dispatched entry point** that is safe to intercept, and leave the interface
alone:

```csharp
// intercepted when the pair is resolvable in this compilation; falls back to the registry otherwise
DwarfMap.To<OrderDto>(order);          // or
DwarfMap.Map<Order, OrderDto>(order);
```

- A static call has no receiver to bypass, so no user implementation can be shadowed.
- The generator can intercept exactly the call sites whose pair it can resolve, and leave the rest to the
  registry — no all-or-nothing switch.
- `IDwarfMapper` keeps its current semantics for DI, decorators and cross-assembly use, unchanged.

Cost: one new public entry point, which the architecture tests now require to be classified
(`RuntimeSurfaceArchitectureTests.Every_public_type_declares_how_it_behaves_at_run_time`) — and it would be the
first member classified as *compile-time bound with a runtime fallback*, a fifth bucket.

## What it cannot reach, and therefore what the registry is still for

- `Map<TDestination>(object)` — the source type arrives with the instance. Irreducible.
- Genuine cross-assembly use where the consumer has **no reference** to the declaring assembly. Interception
  needs a callable target; without a reference there is none. This is the registry's actual purpose and it stays.
- Call sites in assemblies that do not run the generator.

So the end state is not "no registry". It is: the registry serves the case that genuinely cannot be decided at
compile time, and nothing else routes through it.

## The unverified question, named so it is not assumed

**Is `InterceptsLocation` stable and usable on this target, without a preview feature gate?** It shipped as an
experimental feature, its location-encoding changed once, and a library that ships a generator cannot require
consumers to set `<InterceptorsPreviewNamespaces>`. Verify before any of the above is planned:

1. does a `net10.0` consumer compile an intercepted call with no extra property set;
2. does the interceptor survive the `DwarfMapper.ConsumerTests` reference shapes (project reference, package
   reference, no reference);
3. does it interact with `PublicAPI` analyzer tracking and with AOT publish.

If the answer to (1) is no, this proposal is dead for now and the honest interim is the one below.

## The interim, if compile-time binding is not available

Keep the simplified slots and **adjudicate their mutants as proven-equivalent in the ledger**, with the proof
stated once: *a cache has no observable behaviour, so no test can distinguish its machinery from its absence.*
That is a rigorous proof, not an exemption — it is the same category the depth-clamp boundary rows already sit
in. Then re-pin `break` from the measured score.

This is the one place that needs an owner ruling, because it means a floor moving **down**, and round 30 set the
opposite precedent (`e01ff23`: rather than lower the runtime floor, the owner deleted the unkillable
hand-written `Key` equality). The distinction worth weighing: that code bought nothing, and this buys a measured
1.25–1.66x on the ambient path. Deleting the slots is also a legitimate answer — it returns the facade to
18.17 ns, still ~3x ahead of AutoMapper, and returns the leg to its old ceiling.
