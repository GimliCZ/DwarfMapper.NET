// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Core;

/// <summary>
///     Deterministic FNV-1a hashing, in one place. These hashes feed GENERATED HELPER NAMES
///     (<c>__DwarfMapObj_&lt;hash&gt;</c>, <c>__DwarfMap_Coll_&lt;hash&gt;</c>, …), so they must be stable across
///     processes and machines — <c>string.GetHashCode()</c> is randomised per process and must never be used here.
/// </summary>
internal static class StableHash
{
    private const uint Offset = 2166136261u;
    private const uint Prime = 16777619u;

    /// <summary>
    ///     FNV-1a over UTF-16 code units (one round per char). This is the form nine call sites already used
    ///     byte-for-byte, so routing them here changes no generated name.
    /// </summary>
    public static string Fnv1a(string s)
    {
        unchecked
        {
            var h = Offset;
            foreach (var c in s)
            {
                h ^= c;
                h *= Prime;
            }

            return h.ToString("x8", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    ///     FNV-1a over BYTES (two rounds per char: low byte, then high byte). Used only by
    ///     <c>NestedMappingRegistry</c>.
    ///     <para>
    ///     This deliberately differs from <see cref="Fnv1a" /> and is kept rather than unified. Both feed
    ///     generated helper names, so converging them would RENAME helpers across the whole golden corpus — a
    ///     large, noisy manifest diff for zero behavioural benefit. Keeping both here, named and documented,
    ///     turns what was an accidental divergence across ten files into a deliberate one in a single file.
    ///     </para>
    /// </summary>
    public static string Fnv1aPerByte(string s)
    {
        unchecked
        {
            var hash = Offset;
            foreach (var c in s)
            {
                hash ^= (byte)(c & 0xFF);
                hash *= Prime;
                hash ^= (byte)(c >> 8);
                hash *= Prime;
            }

            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
