// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public sealed class Envelope<T>
    {
        public T Payload { get; set; } = default!;

        public int Status { get; set; }

        public string CorrelationId { get; set; } = "";
    }

    public sealed class WmUser
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    public sealed class WmUserDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    public sealed class WmOrder
    {
        public long Total { get; set; }
    }

    public sealed class WmOrderDto
    {
        public long Total { get; set; }
    }

    /// <summary>
    ///     The application-layer envelope in its <b>nullable</b> payload spelling: the failure arm carries no
    ///     payload and says so in the type. Round 29 task 2.6.
    /// </summary>
    public sealed class WmResult<T>
        where T : class
    {
        public WmResult(T? value, string? error)
        {
            Value = value;
            Error = error;
        }

        public T? Value { get; }

        public string? Error { get; }
    }

    /// <summary>
    ///     The same envelope in its <b>non-nullable</b> payload spelling — the <c>Ardalis.Result</c> shape, whose
    ///     failure arm parks <c>default!</c> in a member the annotation says cannot be null. Mapping a failed one
    ///     used to throw <see cref="ArgumentNullException" /> out of the payload map's own entry guard. The
    ///     <c>Ok</c>/<c>Fail</c> factories the real type carries are inlined at the call sites here: CA1000 is an
    ///     error in this project, and the factories are not what the map reads.
    /// </summary>
    public sealed class WmOutcome<T>
        where T : class
    {
        public WmOutcome(T value, string? error)
        {
            Value = value;
            Error = error;
        }

        public T Value { get; }

        public string? Error { get; }
    }

    [DwarfMapper]
    [GenerateMap<WmUser, WmUserDto>]
    [GenerateMap<WmOrder, WmOrderDto>]
    [GenerateWrapperMap(typeof(Envelope<>))] // also synthesizes Envelope<WmUser>->Envelope<WmUserDto> and Envelope<WmOrder>->Envelope<WmOrderDto>
    [GenerateWrapperMap(typeof(WmResult<>))]
    [GenerateWrapperMap(typeof(WmOutcome<>))]
    public partial class WrapperMappers
    {
    }

    /// <summary>
    ///     The same two envelopes, but with the payload pair declared as a partial METHOD rather than as a
    ///     <c>[GenerateMap]</c> pair. That is the difference the round 29 task 2.6 defect turned on, and it is the
    ///     COMMON way to write it: the payload edge then resolves to the user's own <c>ToDto</c> — which, being a
    ///     public entry point, opens with <c>ArgumentNullException.ThrowIfNull</c> — instead of to a synthesized
    ///     <c>__DwarfMap_Obj_</c> helper, which has answered a null with a null since it was written. Whether a
    ///     nested edge preserved a null or threw therefore depended on which of the two the resolver happened to
    ///     pick. <see cref="WrapperMappers" /> above takes the synthesized branch, so it cannot see the defect;
    ///     this class is the one that does.
    /// </summary>
    [DwarfMapper]
    [GenerateWrapperMap(typeof(WmResult<>))]
    [GenerateWrapperMap(typeof(WmOutcome<>))]
    [GenerateMap<WmUser, WmUserDto>]
    public partial class DeclaredPayloadWrapperMappers
    {
        public partial WmUserDto ToDto(WmUser user);
    }

    public class WrapperMapRuntimeTests
    {
        [Fact]
        public void Wrapper_map_is_synthesized_for_each_declared_payload_pair()
        {
            var m = new WrapperMappers();

            var userEnv = new Envelope<WmUser>
            {
                Payload = new WmUser
                {
                    Id = 7,
                    Name = "ada"
                },
                Status = 200,
                CorrelationId = "abc"
            };
            var dto = m.Map(userEnv);

            Assert.Equal(7, dto.Payload.Id);
            Assert.Equal("ada", dto.Payload.Name);
            Assert.Equal(200, dto.Status);
            Assert.Equal("abc", dto.CorrelationId);

            // The second declared payload pair is wrapped too.
            var orderEnv = new Envelope<WmOrder>
            {
                Payload = new WmOrder
                {
                    Total = 99
                },
                Status = 201
            };
            var orderDto = m.Map(orderEnv);
            Assert.Equal(99, orderDto.Payload.Total);
            Assert.Equal(201, orderDto.Status);

            // The inner (unwrapped) maps still work alongside the wrapper maps.
            Assert.Equal("ada",
                m.Map(new WmUser
                {
                    Id = 1,
                    Name = "ada"
                }).Name);
        }

        // ── Round 29 task 2.6: the payload edge has a null policy, and it is the same one everywhere ────────

        [Fact]
        public void A_failed_result_with_a_nullable_payload_maps_to_a_failed_result()
        {
            var mapped = new WrapperMappers().Map(new WmResult<WmUser>(null, "not found"));

            Assert.Null(mapped.Value);
            Assert.Equal("not found", mapped.Error);
        }

        [Fact]
        public void A_failed_result_with_a_NON_nullable_payload_maps_instead_of_throwing()
        {
            // The annotation says WmOutcome<T>.Value cannot be null; Fail stores default! and makes that false.
            // Before the fix this threw ArgumentNullException out of the generated payload map's entry guard, so
            // an envelope whose whole purpose is "no payload, here is why" could not be mapped at all.
            var mapped = new WrapperMappers().Map(new WmOutcome<WmUser>(default!, "not found"));

            Assert.Null(mapped.Value);
            Assert.Equal("not found", mapped.Error);
        }

        [Fact]
        public void The_success_arm_of_both_spellings_still_maps_its_payload()
        {
            var m = new WrapperMappers();
            var user = new WmUser
            {
                Id = 7,
                Name = "ada"
            };

            Assert.Equal("ada", m.Map(new WmResult<WmUser>(user, null)).Value!.Name);
            Assert.Equal("ada", m.Map(new WmOutcome<WmUser>(user, null)).Value.Name);
        }

        [Fact]
        public void A_declared_payload_map_preserves_null_on_the_nullable_spelling()
        {
            // The arm that used to emit CS8604 into the consumer's .g.cs: nullable payload, nullable destination,
            // converter = the user's own null-refusing ToDto.
            var mapped = new DeclaredPayloadWrapperMappers().Map(new WmResult<WmUser>(null, "not found"));

            Assert.Null(mapped.Value);
            Assert.Equal("not found", mapped.Error);
        }

        [Fact]
        public void A_declared_payload_map_no_longer_throws_on_the_non_nullable_spelling()
        {
            // The arm that used to throw ArgumentNullException out of ToDto's own entry guard.
            var mapped = new DeclaredPayloadWrapperMappers().Map(new WmOutcome<WmUser>(default!, "not found"));

            Assert.Null(mapped.Value);
            Assert.Equal("not found", mapped.Error);
        }

        [Fact]
        public void A_declared_payload_map_still_maps_a_present_payload()
        {
            var m = new DeclaredPayloadWrapperMappers();
            var user = new WmUser
            {
                Id = 7,
                Name = "ada"
            };

            Assert.Equal("ada", m.Map(new WmResult<WmUser>(user, null)).Value!.Name);
            Assert.Equal("ada", m.Map(new WmOutcome<WmUser>(user, null)).Value.Name);
            Assert.Equal(7, m.ToDto(user).Id);
        }

        [Fact]
        public void A_null_payload_in_the_original_envelope_maps_to_a_null_payload()
        {
            // Envelope<T> is the non-nullable spelling too (`= default!`), and this is the same guard seen
            // through the shape this file already had.
            var mapped = new WrapperMappers().Map(new Envelope<WmUser>
            {
                Payload = null!,
                Status = 404,
                CorrelationId = "abc"
            });

            Assert.Null(mapped.Payload);
            Assert.Equal(404, mapped.Status);
            Assert.Equal("abc", mapped.CorrelationId);
        }
    }
}
