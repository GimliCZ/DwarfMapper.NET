// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;

namespace DwarfMapper.IntegrationTests;

// ISSUE-005 — a key CONVERSION can be many-to-one, so two distinct SOURCE keys can land on one TARGET key.
// The mutable path emitted `__r[key] = value` (last-write-wins); the immutable path used
// ImmutableDictionary.CreateRange, which THROWS on a duplicate key. Same mapping, same data, opposite outcome
// decided only by whether the destination type happened to be immutable.
//
// The collision is driven by an ALIASED enum: AliasKey.A and AliasKey.B are both 1, so parsing the source keys
// "A" and "B" by name yields two equal target keys.

#pragma warning disable CA1069 // Duplicate enum value is the POINT of this fixture: it is what makes the key
                               // conversion many-to-one and therefore able to collide.
public enum AliasKey
{
    A = 1,
    B = 1, // deliberate alias — equal to A as a dictionary key
}
#pragma warning restore CA1069

public class CollisionSrc
{
    public Dictionary<string, int> Values { get; set; } = new();
}

public class CollisionMutableDst
{
    public Dictionary<AliasKey, int> Values { get; set; } = new();
}

public class CollisionImmutableDst
{
    public ImmutableDictionary<AliasKey, int> Values { get; set; } = ImmutableDictionary<AliasKey, int>.Empty;
}

[DwarfMapper(EnumStrategy = EnumStrategy.ByName)]
public partial class CollisionMutableMapper
{
    public partial CollisionMutableDst Map(CollisionSrc s);
}

[DwarfMapper(EnumStrategy = EnumStrategy.ByName)]
public partial class CollisionImmutableMapper
{
    public partial CollisionImmutableDst Map(CollisionSrc s);
}

public class DictionaryKeyCollisionRuntimeTests
{
    // "A" and "B" both parse to AliasKey value 1 → one target key, two source entries.
    private static CollisionSrc Colliding()
    {
        return new CollisionSrc { Values = { ["A"] = 1, ["B"] = 2 } };
    }

    [Fact]
    public void An_immutable_dictionary_target_does_not_throw_on_a_colliding_key()
    {
        // Before the fix this threw ArgumentException ("An item with the same key has already been added")
        // out of ImmutableDictionary.CreateRange.
        var dst = new CollisionImmutableMapper().Map(Colliding());

        Assert.Single(dst.Values);
    }

    [Fact]
    public void Mutable_and_immutable_targets_agree_on_a_colliding_key()
    {
        var mutable = new CollisionMutableMapper().Map(Colliding());
        var immutable = new CollisionImmutableMapper().Map(Colliding());

        Assert.Equal(mutable.Values.Count, immutable.Values.Count);
        Assert.Equal(mutable.Values[AliasKey.A], immutable.Values[AliasKey.A]);
    }

    [Fact]
    public void A_non_colliding_dictionary_still_maps_every_entry()
    {
        // Over-reach guard: the builder must not drop distinct keys.
        var src = new CollisionSrc { Values = { ["A"] = 7 } };

        Assert.Equal(7, new CollisionImmutableMapper().Map(src).Values[AliasKey.A]);
        Assert.Equal(7, new CollisionMutableMapper().Map(src).Values[AliasKey.A]);
    }
}
