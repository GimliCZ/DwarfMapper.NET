// SPDX-License-Identifier: GPL-2.0-only
// CASE: An [AfterMap] hook takes its target by ref, declared against a BASE type — and a
//       [MapDerivedType] arm's own declared pair has a destination that is a DERIVED type only
//       implicitly convertible (by value) to that base, not identical to it.
// WHY:  Round 30 coverage sweep. Every HookCall construction site matches a hook to a pair by
//       implicit-conversion ("does the destination convert TO the hook's declared parameter type?"),
//       which is correct for an ORDINARY by-value parameter but wrong once the parameter is `ref`: C#
//       has no ref covariance, so `ref DogDto` does not bind to a `ref AnimalDto` parameter even
//       though DogDto converts to AnimalDto by value. The hook matches the [MapDerivedType] dispatch
//       method itself perfectly (its local really is AnimalDto) but ALSO matches Dog's own declared
//       pair (whose local is DogDto) by the same by-value rule — which used to emit CS0037's sibling,
//       CS1503, in a .g.cs no consumer can edit. The generator now skips the hook for the mismatched
//       pair only (Error, ScopedToMethod: the dispatch method and any pair whose destination truly is
//       AnimalDto keep calling it).
// EXPECT: DWARF109
// EXPECT-MESSAGE DWARF109: 'Finish' takes its target by 'ref'
// EXPECT-MESSAGE DWARF109: does not exactly match this pair's destination type
// EXPECT-MESSAGE DWARF109: ref global::Demo.AnimalDto
// EXPECT-MESSAGE DWARF109: global::Demo.DogDto
// EXPECT-CS:
// NOTE: No CS at all — that is the fix. Map(Animal a) (the dispatch method) still calls
//       Finish(ref __dwarf_target) normally; Map(Dog d) (the arm's own declared pair) compiles clean
//       without calling it. Before this fix, this exact fixture emitted CS1503 in a .g.cs no
//       consumer can edit.

using DwarfMapper;

namespace Demo;

public abstract class Animal
{
    public string Name { get; set; } = "";
}

public class Dog : Animal
{
    public string Breed { get; set; } = "";
}

public class AnimalDto
{
    public string Name { get; set; } = "";
}

public class DogDto : AnimalDto
{
    public string Breed { get; set; } = "";
}

[DwarfMapper]
public partial class M
{
    [MapDerivedType<Dog, DogDto>]
    public partial AnimalDto Map(Animal a);

    public partial DogDto Map(Dog d);

    [AfterMap]
    private static void Finish(ref AnimalDto d)
    {
    }
}
