// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public class NeSrc
    {
        public List<int?> Vals { get; set; } = new();

        public List<string?> Refs { get; set; } = new();

        public string?[] Arr { get; set; } = Array.Empty<string?>();
    }

    public class NeDst
    {
        public List<int> Vals { get; set; } = new();

        public List<string> Refs { get; set; } = new();

        public string[] Arr { get; set; } = Array.Empty<string>();
    }

// Nullable-element source collections mapped to non-nullable-element targets. This must COMPILE (the
// reference-element case previously produced CS8620 / a silent null-passing array clone) and must throw
// loudly on an actual null element rather than smuggle it into the non-null target.
    [DwarfMapper]
    [GenerateMap<NeSrc, NeDst>]
    public partial class NullableElemMapper
    {
    }

    public class NullableElementRuntimeTests
    {
        [Fact]
        public void Maps_nullable_element_collections_to_non_null_when_no_nulls_present()
        {
            var d = new NullableElemMapper().Map(new NeSrc
            {
                Vals = new List<int?>
                {
                    1,
                    2
                },
                Refs = new List<string?>
                {
                    "a",
                    "b"
                },
                Arr = new[]
                {
                    "x"
                }
            });

            Assert.Equal(new[]
                {
                    1, 2
                },
                d.Vals);
            Assert.Equal(new[]
                {
                    "a", "b"
                },
                d.Refs);
            Assert.Equal(new[]
                {
                    "x"
                },
                d.Arr);
        }

        [Fact]
        public void Throws_on_null_reference_element_into_non_null_list()
        {
            var src = new NeSrc
            {
                Refs = new List<string?>
                {
                    "a",
                    null
                }
            };
            Assert.Throws<InvalidOperationException>(() => new NullableElemMapper().Map(src));
        }

        [Fact]
        public void Throws_on_null_reference_element_into_non_null_array()
        {
            var src = new NeSrc
            {
                Arr = new[]
                {
                    "a", null
                }
            };
            Assert.Throws<InvalidOperationException>(() => new NullableElemMapper().Map(src));
        }

        [Fact]
        public void Throws_on_null_value_element_into_non_null_list()
        {
            var src = new NeSrc
            {
                Vals = new List<int?>
                {
                    1,
                    null
                }
            };
            Assert.Throws<InvalidOperationException>(() => new NullableElemMapper().Map(src));
        }

        // ── The bounds-check-elision fast path, at runtime (round 29 T0.2d) ────────────────────────────────
        // The array→array arm indexes with a single length-bounded counter so the JIT elides both bounds
        // checks. For a LIFTED element (Nullable<struct>, or a nullable reference through a synthesized helper)
        // that arm now binds the element to a local instead of substituting the indexer into both reads of the
        // shared expression — the CS8629 fix. The compiler warning was the visible half; this is the half that
        // matters: the loop must still visit every slot, in order, keeping nulls null and mapping values.

        [Fact]
        public void Elision_path_keeps_null_slots_null_and_maps_values_for_a_nullable_struct_element()
        {
            var d = new ElisionMapper().Map(new ElSrc
            {
                Points =
                [
                    new ElPoint { X = 1, Y = 2 },
                    null,
                    new ElPoint { X = 3, Y = 4 },
                    null
                ]
            });

            Assert.Equal(4, d.Points.Length);
            Assert.Equal(1, d.Points[0]!.Value.X);
            Assert.Equal(2, d.Points[0]!.Value.Y);
            Assert.Null(d.Points[1]);
            Assert.Equal(3, d.Points[2]!.Value.X);
            Assert.Equal(4, d.Points[2]!.Value.Y);
            Assert.Null(d.Points[3]);
        }

        [Fact]
        public void Elision_path_keeps_null_slots_null_and_maps_values_for_a_nullable_reference_element()
        {
            // The other arm the same rule covers: a nullable REFERENCE element through a synthesized object
            // helper. Its expression also reads the element twice, so it takes the local binding too.
            var d = new ElisionMapper().Map(new ElSrc
            {
                Children =
                [
                    new ElChild { V = 7 },
                    null,
                    new ElChild { V = 9 }
                ]
            });

            Assert.Equal(3, d.Children.Length);
            Assert.Equal(7, d.Children[0]!.V);
            Assert.Null(d.Children[1]);
            Assert.Equal(9, d.Children[2]!.V);
        }

        [Fact]
        public void Elision_path_maps_an_empty_array_to_an_empty_array()
        {
            var d = new ElisionMapper().Map(new ElSrc());
            Assert.Empty(d.Points);
            Assert.Empty(d.Children);
        }
    }

    // ── Types for the elision-path tests above ────────────────────────────────────────────────────────────
    // The element pairs are DISTINCT types on purpose: an identity element pair takes Array.Clone() and never
    // reaches the lifted per-element expression at all.

    public struct ElPoint
    {
        public int X { get; set; }

        public int Y { get; set; }
    }

    public struct ElPointDto
    {
        public int X { get; set; }

        public int Y { get; set; }
    }

    public class ElChild
    {
        public int V { get; set; }
    }

    public class ElChildDto
    {
        public int V { get; set; }
    }

    public class ElSrc
    {
        public ElPoint?[] Points { get; set; } = Array.Empty<ElPoint?>();

        public ElChild?[] Children { get; set; } = Array.Empty<ElChild?>();
    }

    public class ElDst
    {
        public ElPointDto?[] Points { get; set; } = Array.Empty<ElPointDto?>();

        public ElChildDto?[] Children { get; set; } = Array.Empty<ElChildDto?>();
    }

    [DwarfMapper]
    [GenerateMap<ElSrc, ElDst>]
    public partial class ElisionMapper
    {
    }
}
