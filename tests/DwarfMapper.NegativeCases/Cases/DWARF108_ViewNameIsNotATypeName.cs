// SPDX-License-Identifier: GPL-2.0-only
// CASE: [GenerateView(Name = "3 dogs")] — round 29, Phase 1, review round 1
// WHY:  The Name becomes the nested `readonly ref struct`'s type name, written into generated C# in three
//       positions: the struct declaration, its constructor, and the factory's return type. A value that
//       is not an identifier therefore does not fail at the attribute; it fails as CS1001/CS1514/CS1513
//       inside a .g.cs the consumer cannot edit and did not write, for an ordinary typing mistake. That
//       is the unsuppressible-diagnostic class round 29 exists to clear, appearing in code the round
//       itself shipped.
//
//       Measured before the check was written: `Name = "3 dogs"` emitted
//       `public readonly ref struct 3 dogs` with no generator diagnostic at all.
//
//       WHAT IS NOT REFUSED, and this is the point of the design rather than a footnote: a KEYWORD.
//       `Name = "class"` and `Name = "record"` are accepted and emitted escaped — `@class`, `@record` —
//       because `@` is C#'s own mechanism for using a keyword as an identifier. The repository already
//       had Identifiers.Escape for exactly this (b888cc3 used it for a keyword-named mapping parameter);
//       reusing it here removed a refusal instead of adding one. Escaping also dissolves the single case
//       no syntactic check could catch: unescaped `scoped` parses into the same tree as a good name and
//       fails later in the binder, while `@scoped` cannot be read as a modifier at all. A test sweeps
//       every keyword Roslyn knows (127, reserved and contextual) through the generator rather than
//       trusting a list that would go stale.
//
//       So the only question left is "is this an identifier", which SyntaxFacts.IsValidIdentifier answers
//       exactly — no list, nothing over-refused. `Name = ""` is refused under the same rule, and that
//       closes a silence: it used to mean "no name given" and discarded what the consumer wrote.
//
//       LOCATED ON THE ARGUMENT, not on the attribute and not on the class: the offending text is the
//       argument and nothing else on that line is wrong.
//
//       A name that IS an identifier but is already taken by a type on the mapper is DWARF102, not this:
//       the name is valid, the slot is occupied.
// EXPECT: DWARF108
// EXPECT-MESSAGE DWARF108: is not a C# identifier
// EXPECT-MESSAGE DWARF108: Give the view a valid C# identifier
// EXPECT-MESSAGE DWARF108: omit Name to take the default 'DstView'
// EXPECT-MESSAGE DWARF108: A keyword IS accepted

using DwarfMapper;

namespace Demo;

public sealed class Src
{
    public int Id { get; set; }
}

public sealed class Dst
{
    public int Id { get; set; }
}

[DwarfMapper]
[GenerateView<Src, Dst>(Name = "3 dogs")]
public partial class M
{
}
