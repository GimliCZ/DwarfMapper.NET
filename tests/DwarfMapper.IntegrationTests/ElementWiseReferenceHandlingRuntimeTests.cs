// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // The element-wise endpoints (span map, async-stream map) thread the SAME shared DwarfRefContext the
// top-level collection path does: one context per call, shared across every element. These are the
// executing pins for B33 — the surface matrix's EmittedInvalidCode cells proved the emission COMPILED
// wrong (the ctx-tailed element converter was called without its (ctx, depth) tail, CS7036 in the
// generated file), and a matrix cell can only grade "different", not "right" (B19). What "right" means
// here is pinned below: under Preserve, two elements holding the same source object land the SAME
// target instance, because the identity map spans the whole call, not each element.

// ── Preserve: shared identity across elements ────────────────────────────────

    public class EwrChild
    {
        public int V { get; set; }
    }

    public class EwrElem
    {
        public int Id { get; set; }

        public EwrChild? Child { get; set; }
    }

    public class EwrChildDto
    {
        public int V { get; set; }
    }

    public class EwrElemDto
    {
        public int Id { get; set; }

        public EwrChildDto? Child { get; set; }
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class EwrPreserveMapper
    {
        public partial void MapSpan(ReadOnlySpan<EwrElem> src, Span<EwrElemDto> dst);

        public partial IAsyncEnumerable<EwrElemDto> MapStream(IAsyncEnumerable<EwrElem> src);
    }

// ── None (default): a recursive element pair carries the depth guard through the endpoints ──

    public class EwrNode
    {
        public int V { get; set; }

        public EwrNode? Next { get; set; }
    }

    public class EwrNodeDto
    {
        public int V { get; set; }

        public EwrNodeDto? Next { get; set; }
    }

    [DwarfMapper]
    public partial class EwrNoneMapper
    {
        public partial void MapSpan(ReadOnlySpan<EwrNode> src, Span<EwrNodeDto> dst);

        public partial IAsyncEnumerable<EwrNodeDto> MapStream(IAsyncEnumerable<EwrNode> src);
    }

// ── OnCycle = SetNull: the on-stack guard needs its stack set allocated behind the shared ctx ──

    [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
    public partial class EwrSetNullMapper
    {
        public partial void MapSpan(ReadOnlySpan<EwrNode> src, Span<EwrNodeDto> dst);
    }

    public class ElementWiseReferenceHandlingRuntimeTests
    {
        private static async IAsyncEnumerable<T> Source<T>(params T[] items)
        {
            foreach (var i in items)
            {
                await Task.Yield();
                yield return i;
            }
        }

        [Fact]
        public void Preserve_span_maps_the_same_source_element_to_the_same_instance()
        {
            var shared = new EwrElem
            {
                Id = 1,
                Child = new EwrChild
                {
                    V = 7
                }
            };
            var other = new EwrElem
            {
                Id = 2
            };
            var src = new[]
            {
                shared, other, shared
            };
            var dst = new EwrElemDto[3];

            new EwrPreserveMapper().MapSpan(src, dst);

            Assert.Same(dst[0], dst[2]); // one source object → one target instance, across slots
            Assert.NotSame(dst[0], dst[1]);
            Assert.Equal(1, dst[0].Id);
            Assert.Equal(7, dst[0].Child!.V);
            Assert.Equal(2, dst[1].Id);
        }

        [Fact]
        public void Preserve_span_preserves_a_child_shared_between_distinct_elements()
        {
            var child = new EwrChild
            {
                V = 9
            };
            var src = new[]
            {
                new EwrElem
                {
                    Id = 1,
                    Child = child
                },
                new EwrElem
                {
                    Id = 2,
                    Child = child
                }
            };
            var dst = new EwrElemDto[2];

            new EwrPreserveMapper().MapSpan(src, dst);

            Assert.NotSame(dst[0], dst[1]);
            Assert.Same(dst[0].Child, dst[1].Child); // the diamond spans two elements — one context sees it
            Assert.Equal(9, dst[0].Child!.V);
        }

        [Fact]
        public async Task Preserve_stream_maps_the_same_source_element_to_the_same_instance()
        {
            var shared = new EwrElem
            {
                Id = 1,
                Child = new EwrChild
                {
                    V = 7
                }
            };
            var other = new EwrElem
            {
                Id = 2
            };

            var result = new List<EwrElemDto>();
            await foreach (var d in new EwrPreserveMapper().MapStream(Source(shared, other, shared)))
                result.Add(d);

            Assert.Equal(3, result.Count);
            Assert.Same(result[0], result[2]); // the identity map lives as long as the iterator
            Assert.NotSame(result[0], result[1]);
            Assert.Equal(7, result[0].Child!.V);
        }

        [Fact]
        public void None_span_maps_a_recursive_element_pair()
        {
            // Under default None handling a recursive pair's converter carries the (ctx, depth) tail too —
            // the same emission that broke under Preserve broke here, with no Preserve in sight.
            var chain = new EwrNode
            {
                V = 1,
                Next = new EwrNode
                {
                    V = 2
                }
            };
            var src = new[]
            {
                chain
            };
            var dst = new EwrNodeDto[1];

            new EwrNoneMapper().MapSpan(src, dst);

            Assert.Equal(1, dst[0].V);
            Assert.Equal(2, dst[0].Next!.V);
            Assert.Null(dst[0].Next!.Next);
        }

        [Fact]
        public void None_span_throws_the_depth_exception_on_a_cyclic_element()
        {
            var loop = new EwrNode
            {
                V = 1
            };
            loop.Next = loop;

            var thrown = false;
            try
            {
                var src = new[]
                {
                    loop
                };
                var dst = new EwrNodeDto[1];
                new EwrNoneMapper().MapSpan(src, dst);
            }
            catch (DwarfMappingDepthException)
            {
                thrown = true; // bounded, deterministic — the guard fires at MaxDepth, never a hang
            }

            Assert.True(thrown, "a cyclic element under None handling must throw DwarfMappingDepthException");
        }

        [Fact]
        public async Task None_stream_maps_a_recursive_element_pair()
        {
            var chain = new EwrNode
            {
                V = 1,
                Next = new EwrNode
                {
                    V = 2
                }
            };

            var result = new List<EwrNodeDto>();
            await foreach (var d in new EwrNoneMapper().MapStream(Source(chain)))
                result.Add(d);

            Assert.Single(result);
            Assert.Equal(2, result[0].Next!.V);
        }

        [Fact]
        public void SetNull_span_nulls_the_reentrant_back_edge()
        {
            // Proves the shared context is allocated WITH setNull: true — the on-stack guard the element
            // converter runs dereferences the stack set, so a plain depth-guard context would fault here.
            var loop = new EwrNode
            {
                V = 1
            };
            loop.Next = loop;
            var src = new[]
            {
                loop
            };
            var dst = new EwrNodeDto[1];

            new EwrSetNullMapper().MapSpan(src, dst);

            Assert.Equal(1, dst[0].V);
            Assert.Null(dst[0].Next); // the back-edge is cut, not recursed into
        }
    }
}
