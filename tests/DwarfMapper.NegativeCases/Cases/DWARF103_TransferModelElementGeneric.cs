// SPDX-License-Identifier: GPL-2.0-only
// CASE: a GENERIC element type that is transfer-model shaped in every other respect, and which DWARF103 must
//       not name (round 29, T2.3 fix round 2)
// WHY:  This fired until T2.3 fix round 2, and the report was harmful rather than merely uninformative. The
//       element pair below is 'Src' -> 'Box<int>'; the destination is sealed, holds one 'int', and passes
//       every rule TransferModelShape applies, so the hint printed "'Box<int>' is 4 bytes, declare it a
//       readonly record struct".
//
//       There is no such declaration. The only thing a rewrite can change is 'Box<T>', so acting on that
//       advice converts EVERY instantiation — including the 'Box<string>' that 'Holder' below holds, which
//       nothing classified, no diagnostic named, and whose real size is 8 rather than 4. That type would
//       lose its reference identity silently, which is the exact change the classifier exists to refuse,
//       arriving through the remedy the diagnostic printed.
//
//       'Holder' is in the fixture for that reason and is load-bearing: without a SECOND instantiation the
//       damage has nothing to land on, and the case would pass while describing a smaller problem than the
//       one it is about.
//
//       Narrowed in the CLASSIFIER, not only in the code fix that declines the same shape from the other
//       side. DWARF103 has never shipped (AnalyzerReleases.Unshipped.md), so nothing depended on it, and a
//       message saying "this could be a struct" about a type we would then refuse to convert is advice we
//       know to be bad — a non-case rather than a real case waiting for another diagnostic to cover it.
//
//       `EXPECT: none` is the whole assertion, and the set being EXACT is the other half of it: the mapping
//       still SUCCEEDS and must stay complete, so a refusal that went silent by breaking the map instead
//       would surface here as another id rather than as silence.
// EXPECT: none

using System.Collections.Generic;
using DwarfMapper;

namespace Demo;

public sealed class Src
{
    public int Value { get; set; }
}

public sealed class Box<T>
{
    public T Value { get; set; } = default!;
}

/// <summary>The second instantiation — the type the rewrite would have reached without ever naming it.</summary>
public sealed class Holder
{
    public Box<string> Text { get; set; } = new();
}

public class Source
{
    public List<Src> Rows { get; set; } = [];
}

public class Target
{
    public List<Box<int>> Rows { get; set; } = [];
}

[DwarfMapper]
public partial class GenericElementMapper
{
    public partial Target Map(Source source);
}
