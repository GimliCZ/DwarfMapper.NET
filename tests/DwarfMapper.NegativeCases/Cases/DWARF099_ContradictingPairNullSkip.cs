// SPDX-License-Identifier: GPL-2.0-only
// CASE: one pair named twice with OPPOSITE [MapNullSkip<TSource, TTarget>] values (TASKS.md B24)
// WHY:  MapNullSkipAttribute<TSource, TTarget> is AllowMultiple, so this compiled clean and
//       MapperExtractor.ResolveNullSkip returned the FIRST match by declaration order — the second
//       declaration, an explicit and opposite statement of the caller's intent, was dropped without a word.
//       Source order decided whether a patch-merge mapper skips nulls, and swapping the two lines produced a
//       different mapper in silence.
//
//       An ERROR rather than a warning, and the reason is that there is nothing to rank. The two
//       declarations have IDENTICAL scope. That is what separates this from the method-versus-pair
//       contradiction A6 settled as most-specific-wins: those forms have different scopes and therefore a
//       defensible ordering. Here only source order separates them, so the generator refuses instead of
//       resolving by accident.
//
//       What is NOT here: two declarations that AGREE are accepted in silence — a repeated declaration that
//       says the same thing discards nothing. The runner matches the EXPECT set exactly, so a guard that
//       merely counted duplicates would fail the controls in ContradictingPairNullSkipTests.
//
//       DWARF078 accompanies it because DWARF099 is a class-level error: the mapper is refused, and the
//       partial Update method therefore reports CS8795, which DWARF078 exists to signpost.
// EXPECT: DWARF099, DWARF078
// EXPECT-MESSAGE DWARF099: over the SAME pair
// EXPECT-MESSAGE DWARF099: only source order separates them

using DwarfMapper;

namespace Demo;

public class NsSrc
{
    public int Id { get; set; }

    public string? A { get; set; }
}

public class NsDst
{
    public int Id { get; set; }

    public string? A { get; set; }
}

[DwarfMapper]
[MapNullSkip<NsSrc, NsDst>(true)]
[MapNullSkip<NsSrc, NsDst>(false)]
public partial class ContradictingNullSkipMapper
{
    public partial NsDst Update(NsSrc s, NsDst d);
}
