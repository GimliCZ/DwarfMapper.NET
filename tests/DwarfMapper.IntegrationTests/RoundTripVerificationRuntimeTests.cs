// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Testing;

namespace DwarfMapper.IntegrationTests
{
    // ── The consumer-shaped use of [RoundTrip] ────────────────────────────────────
//
// [RoundTrip] is the one part of the surface a CONSUMER reaches for when writing THEIR tests: it emits
// VerifyRoundTrip_<method>(seed, iterations), which fuzzes the forward map and asserts backward(forward(x))
// is x. Everything about it that matters is therefore about what a consumer sees when it fails, and that had
// never been observed outside the package's own suite — which is a helper whose ergonomics were unmeasured.
//
// Both directions are asserted below. A verifier that passed unconditionally would satisfy the first test
// forever and is exactly what a mislinked pair would look like.

    public sealed class RtInvoice
    {
        public int Number { get; set; }

        public string Customer { get; set; } = "";

        public decimal Total { get; set; }
    }

    public sealed class RtInvoiceDto
    {
        public int Number { get; set; }

        public string Customer { get; set; } = "";

        public decimal Total { get; set; }
    }

    /// <summary>A lossless pair: every member survives the trip, so the generated verifier must pass.</summary>
    [DwarfMapper]
    public partial class RtInvoiceMappers
    {
        [RoundTrip]
        public partial RtInvoiceDto ToDto(RtInvoice source);

        public partial RtInvoice FromDto(RtInvoiceDto source);
    }

    public sealed class RtDraft
    {
        public int Number { get; set; }

        public string Note { get; set; } = "";
    }

    public sealed class RtDraftDto
    {
        public int Number { get; set; }

        // The note has no home on the wire. The forward map compiles, the backward map compiles, and the
        // information is gone — which is the mislinking [RoundTrip] exists to catch and which no completeness
        // diagnostic can see, because both halves are individually complete.
        public string Note { get; set; } = "";
    }

    [DwarfMapper]
    public partial class RtDraftMappers
    {
        [RoundTrip]
        [MapIgnore(nameof(RtDraftDto.Note))]
        public partial RtDraftDto ToDto(RtDraft source);

        public partial RtDraft FromDto(RtDraftDto source);
    }

    public class RoundTripVerificationRuntimeTests
    {
        [Fact]
        public void The_generated_verifier_passes_for_a_lossless_pair()
        {
            var ex = Record.Exception(() => new RtInvoiceMappers().VerifyRoundTrip_ToDto(7, 50));

            Assert.Null(ex);
        }

        [Fact]
        public void The_generated_verifier_names_the_member_a_lossy_pair_drops()
        {
            var ex = Assert.Throws<RoundTripException>(() => new RtDraftMappers().VerifyRoundTrip_ToDto(7, 50));

            // The failure has to name the member. "Round trip failed" tells a consumer that something is wrong
            // with a mapper they did not write the body of, which is the least actionable message a generated
            // verifier could produce.
            Assert.Contains(nameof(RtDraft.Note), ex.Message, StringComparison.Ordinal);
        }
    }
}
