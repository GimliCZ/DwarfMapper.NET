// SPDX-License-Identifier: GPL-2.0-only
// CASE: a member carrying [Reinterpret] whose element pair ALSO has a pair-scoped directive — here a
//       [MapIgnore<T>] on the element target (round 29, T0.2d)
// WHY:  DWARF106's second message shape. T0.2c reported the bypass only when the element pair resolved to a
//       conversion the user WROTE; a pair-scoped [MapIgnore<T>] / [MapProperty<S,T>] / [MapValue<T>] /
//       [MapConstructor<S,T>], or a [BeforeMap]/[AfterMap] hook matching the pair, was overridden just as
//       deliberately and just as silently. Everywhere else such a directive keeps the element loop, because
//       NestedMappingRegistry hands the pair ONE synthesized helper and the directive is baked into that
//       helper's body — which a block copy never calls. [Reinterpret] is the one place the block copy wins
//       anyway, because it names THIS member explicitly.
//
//       The consequence is invisible in the output: Ignored is left at its default either way, so nothing in
//       the build tells the author that the [MapIgnore] they wrote is doing nothing for these elements. That
//       is the whole reason the id exists, and it is why the trigger could not stay narrower than the
//       override itself.
//
//       Same id, not a new one: it is the same fact — [Reinterpret] takes precedence over something the pair
//       would otherwise have been given — and a consumer who suppresses it for one shape means it for both.
//       The message differs only in what it names: the pair-scoped directive and the element pair it was
//       declared for, since a directive has no single symbol to point at the way a method does.
//
//       INFORMATIONAL for the same reasons as the conversion shape: an error is a false positive on correct
//       code, and a warning is a build failure under TreatWarningsAsErrors.
//
//       The mapper also maps the SAME element pair as a plain nested member (`One`), which is what keeps this
//       row about DWARF106 alone. That member gets the pair's synthesized helper, so the [MapIgnore] is
//       genuinely applied there — and it makes the point the id exists for visible in one file: the directive
//       works everywhere except behind [Reinterpret], and only DWARF106 says so.
//
//       This row therefore says NOTHING about the DWARF056 sweep: with `One` present the ignore is consumed
//       and 056 is silent either way, so nothing here would notice if the customization question started
//       consuming directives. That property is proved by
//       BlitSoundnessTests.A_pair_directive_nothing_applies_is_still_reported_by_DWARF056, which is where it
//       belongs.
// EXPECT: DWARF106
// EXPECT-MESSAGE DWARF106: takes precedence over the pair-scoped [MapIgnore<T>] declared for
// EXPECT-MESSAGE DWARF106: the block copy fills 'Data' without applying it
// EXPECT-MESSAGE DWARF106: remove [Reinterpret] from 'Data' to use it instead

using DwarfMapper;

namespace Demo;

public struct DirectiveSrc
{
    public int X;

    public int Ignored;
}

public struct DirectiveDst
{
    public int X;

    public int Ignored;
}

public class DirectiveSource
{
    public DirectiveSrc[] Data { get; set; } = [];

    public DirectiveSrc One { get; set; }
}

public class DirectiveTarget
{
    public DirectiveDst[] Data { get; set; } = [];

    public DirectiveDst One { get; set; }
}

[DwarfMapper]
[MapIgnore<DirectiveDst>(nameof(DirectiveDst.Ignored))]
public partial class DirectiveBypassMapper
{
    [Reinterpret("Data")]
    public partial DirectiveTarget Map(DirectiveSource s);
}
