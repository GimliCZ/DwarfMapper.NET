// SPDX-License-Identifier: GPL-2.0-only
// CASE: [AfterMap] on the partial UPDATE-INTO mapping method — the shape that generated infinite recursion
// WHY:  Hook collection scanned every method on the mapper class and accepted anything whose signature fitted.
//       `void Update(Src, Dst)` fits the two-parameter after-hook shape exactly, so it was registered as a
//       hook AND emitted as the mapping method: the generated body of Update ended in `Update(s, d);`,
//       unconditional infinite recursion that compiled, with nothing in the build saying so. The surface
//       matrix scored that cell Honoured, because a cell turns green when the output CHANGES, not when it is
//       right (finding D16).
//
//       The rule needs no special case for mapping methods. C# erases a partial method with no implementing
//       part along with every call to it, so as a hook it can only ever be a no-op; and where this generator
//       supplies the missing part, the call re-enters the method being generated. Refused before the
//       signature is looked at, so ONE diagnostic covers all five endpoints — where the signature filter
//       alone gave three different answers to one mistake (DWARF018 on a non-void create map, a registered
//       but never-invoked hook on a span map, recursion here).
// EXPECT: DWARF091
// EXPECT-MESSAGE DWARF091: [AfterMap] is written on 'Update'
// EXPECT-MESSAGE DWARF091: partial method with no implementing part
// EXPECT-MESSAGE DWARF091: re-enter the method being generated
// EXPECT-MESSAGE DWARF091: void Hook(TTarget) or void Hook(TSource, TTarget)

using DwarfMapper;

namespace Demo;

public sealed class HookSrc
{
    public int Id { get; set; }
}

public sealed class HookDst
{
    public int Id { get; set; }
}

[DwarfMapper]
public partial class HookOnPartialMapper
{
    [AfterMap]
    public partial void Update(HookSrc s, HookDst d);
}
