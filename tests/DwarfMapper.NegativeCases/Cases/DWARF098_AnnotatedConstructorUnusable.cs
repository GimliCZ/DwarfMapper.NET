// SPDX-License-Identifier: GPL-2.0-only
// CASE: [DwarfMapperConstructor] on a PRIVATE constructor — a directive the selector reads, declines, and
//       used to discard in silence (TASKS.md B31)
// WHY:  ConstructorSelector.IsUsableCandidate filters an annotated constructor that is inaccessible,
//       [Obsolete], a record copy constructor, or takes ref/out parameters, and selection then falls back to
//       the parameterless object-initializer path. That FALLBACK IS RIGHT — selecting the constructor would
//       emit a call the compiler rejects — so the emission is deliberately unchanged and this is a Warning,
//       not an Error. What was missing is the report: measured at A14, the output was byte-identical to the
//       unannotated baseline at CreateMap and at Projection, and a caller who marked a private constructor
//       had no way to learn why they got object-initializer mapping.
//
//       The message carries the SPECIFIC filter that rejected it and the remedy for that filter. For a
//       private constructor the remedy is NOT "set AllowNonPublic": that option widens the candidate filter
//       only as far as the consumer's own assembly can see, and a private member of another type never is —
//       measured, after a first draft of this message got it wrong.
//
//       What is NOT here: nothing else. An annotation that IS usable is used and says nothing, and an
//       ABSENT annotation says nothing either — nothing was written, so nothing was discarded. The
//       NegativeCases runner matches the EXPECT set exactly, so a regression that starts reporting the
//       ordinary cases fails this case.
// EXPECT: DWARF098
// EXPECT-MESSAGE DWARF098: [DwarfMapperConstructor] on 'CtorDst(int)' is ignored
// EXPECT-MESSAGE DWARF098: it is private
// EXPECT-MESSAGE DWARF098: no mapper option can reach

using DwarfMapper;

namespace Demo;

public class CtorSrc
{
    public int A { get; set; }
}

public class CtorDst
{
    public CtorDst()
    {
    }

    // Annotated, and unusable: a private constructor of another type cannot be called from the mapper.
    [DwarfMapperConstructor]
    private CtorDst(int a) => A = a;

    public int A { get; set; }
}

[DwarfMapper]
public partial class UnusableAnnotatedCtorMapper
{
    public partial CtorDst Map(CtorSrc s);
}
