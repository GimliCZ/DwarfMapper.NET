// SPDX-License-Identifier: GPL-2.0-only

// 45 — [GenerateWrapperMap]: one declaration covers every payload inside a wrapper.
// Envelopes, results and paged responses all have the same shape: W<A> -> W<B> for whatever A -> B you
// already map. Declaring the open wrapper once synthesises the closed instantiation for every pair on the
// class, instead of a near-identical wrapper map per payload.

namespace DwarfMapper.Gallery.Ex45
{
    public sealed class Envelope<T>
        where T : class
    {
        public T Payload { get; set; } = default!;

        public string TraceId { get; set; } = "";
    }

    public sealed class Pick
    {
        public string Head { get; set; } = "";
    }

    public sealed class PickDto
    {
        public string Head { get; set; } = "";
    }

// <snippet: wrapper-maps>
    [DwarfMapper]
    [GenerateMap<Pick, PickDto>]
    [GenerateWrapperMap(typeof(Envelope<>))]
    public partial class Mapper
    {
        // Envelope<Pick> -> Envelope<PickDto> is synthesised from the pair above; no second declaration.
        public partial Envelope<PickDto> ToDto(Envelope<Pick> envelope);
    }
// </snippet>

    [DocExample(45,
        Tier.Advanced,
        "Wrapper maps",
        Shows = "synthesising W<A> to W<B> for every declared payload pair")]
    public static class Example
    {
        public static void Run()
        {
            var dto = new Mapper().ToDto(new Envelope<Pick>
            {
                Payload = new Pick { Head = "adamant" },
                TraceId = "t-42"
            });
            Console.WriteLine($"45 Wrapper map        -> payload {dto.Payload.Head}, trace {dto.TraceId}");
        }
    }
}
