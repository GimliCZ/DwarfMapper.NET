// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;

namespace DwarfMapper.Generator.Tests.Core;

/// <summary>
///     <see cref="StableHash" /> produces the suffixes in generated helper names (<c>__DwarfMapObj_&lt;hash&gt;</c>,
///     <c>__DwarfMap_Coll_&lt;hash&gt;</c>, …) from about ten call sites. Its own documentation states the values
///     must be stable "across processes and machines" — but nothing pinned them, so the contract was enforced by
///     nobody.
///     <para>
///         That is a real exposure rather than a theoretical one. Every generated name flows into the golden
///         corpus and the snapshot suite, so a change here renames helpers everywhere at once: the failure
///         presents as a giant unexplained manifest diff, and the natural reading of a giant diff is "the
///         snapshots are stale, re-accept them" — which silently launders the regression. Worse, the file itself
///         invites the edit, documenting that its two variants differ only for historical reasons.
///     </para>
///     <para>
///         So these tests pin the actual bytes. The vectors for <c>Fnv1a</c> are the PUBLISHED FNV-1a 32-bit
///         reference values, which additionally proves the implementation is real FNV-1a and not merely
///         self-consistent — a test that only compared the function to itself would pass just as happily on a
///         broken hash.
///     </para>
/// </summary>
public class StableHashTests
{
    [Theory]
    // Canonical FNV-1a 32-bit reference vectors (offset basis 2166136261, prime 16777619).
    [InlineData("", "811c9dc5")] // empty input yields the offset basis
    [InlineData("a", "e40c292c")]
    [InlineData("abc", "1a47e90b")]
    // A realistic key of the shape the converters actually hash.
    [InlineData("global::Demo.Src=>global::Demo.Dst", "313eccc3")]
    public void Fnv1a_matches_the_published_reference_vectors(string input, string expected)
    {
        Assert.Equal(expected, StableHash.Fnv1a(input));
    }

    [Theory]
    // The per-byte variant hashes low byte then high byte, so it diverges from Fnv1a for every non-empty input.
    // Pinned because NestedMappingRegistry's helper names depend on exactly these bytes.
    [InlineData("", "811c9dc5")]
    [InlineData("a", "2b24d044")]
    [InlineData("abc", "ae1e997d")]
    [InlineData("global::Demo.Src=>global::Demo.Dst", "c5062217")]
    public void Fnv1aPerByte_is_pinned(string input, string expected)
    {
        Assert.Equal(expected, StableHash.Fnv1aPerByte(input));
    }

    [Fact]
    public void The_two_variants_stay_deliberately_different()
    {
        // Documented as an intentional divergence kept to avoid renaming the whole golden corpus. If someone
        // "unifies" them, this fails loudly here instead of surfacing as a corpus-wide manifest diff.
        Assert.NotEqual(StableHash.Fnv1a("abc"), StableHash.Fnv1aPerByte("abc"));
    }

    [Fact]
    public void Non_ascii_exercises_the_high_byte_path()
    {
        // 'Ä' is U+00C4: one UTF-16 unit whose high byte is zero, and 'string.GetHashCode' would be randomised
        // per process for it. Pinning both variants proves the char is consumed as documented (one round vs two).
        Assert.Equal("410b2893", StableHash.Fnv1a("Ä"));
        Assert.Equal("f790df69", StableHash.Fnv1aPerByte("Ä"));
    }

    [Fact]
    public void Output_is_always_eight_lowercase_hex_chars()
    {
        // The value is spliced straight into a C# identifier, so width and case are part of the contract:
        // a shorter or upper-cased rendering would change every generated name and could collide.
        foreach (var s in new[] { "", "a", "zzzz", "global::A.B<C>=>global::D.E", new string('x', 512) })
        foreach (var h in new[] { StableHash.Fnv1a(s), StableHash.Fnv1aPerByte(s) })
        {
            Assert.Equal(8, h.Length);
            Assert.All(h, c => Assert.True(
                (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'),
                $"'{h}' must be lowercase hex; '{c}' is not."));
        }
    }

    [Fact]
    public void Hashing_is_deterministic_within_the_process()
    {
        // Guards the one mistake the class exists to prevent: reaching for string.GetHashCode, which is
        // randomised per process and would make generated names differ between builds.
        const string key = "global::Demo.Order=>global::Demo.OrderDto|nn";

        Assert.Equal(StableHash.Fnv1a(key), StableHash.Fnv1a(key));
        Assert.Equal(StableHash.Fnv1aPerByte(key), StableHash.Fnv1aPerByte(key));
        Assert.NotEqual(StableHash.Fnv1a(key), StableHash.Fnv1a(key + "x"));
    }
}
