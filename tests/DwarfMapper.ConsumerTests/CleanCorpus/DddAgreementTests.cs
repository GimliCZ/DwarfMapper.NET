// SPDX-License-Identifier: GPL-2.0-only

using AutoMapper;
using CleanCorpus.Ddd;

namespace CleanCorpus
{
    /// <summary>
    ///     The DDD half of the corpus, converted and compared.
    /// </summary>
    /// <remarks>
    ///     These are the shapes that make mapping hard in applications that are not textbooks — identity that is
    ///     not a primitive, a child collection a mapper is not allowed to assign, construction closed by design,
    ///     a base class every entity carries and no view wants. Each is a deliberate domain decision, and each is
    ///     something a reflective mapper walks straight through without asking.
    /// </remarks>
    public class DddAgreementTests
    {
        private static readonly IMapper Auto =
            new MapperConfiguration(c => c.AddProfile<DddProfile>()).CreateMapper();

        private static readonly DateTimeOffset Start = new(2026, 9, 14, 9, 30, 0, TimeSpan.Zero);

        private static Session ASession()
        {
            var session = Session.Announce(
                "Compile-time mapping",
                new SpeakerId(new Guid("6f1d2e7a-3b44-4c19-9a0e-5d2f8c71b043")),
                new TimeWindow(Start, Start.AddMinutes(45)),
                new Money(129.5m, "GBP"));

            session.Schedule(new Slot("Hall A", new TimeWindow(Start, Start.AddMinutes(45))));
            session.Broadcast(Channel.Livestream);

            // The audited members every entity carries and no view wants.
            session.CreatedAt = Start.AddDays(-30);
            session.CreatedBy = "scheduler";
            session.IsDeleted = false;

            return session;
        }

        [Fact]
        public void A_strongly_typed_id_is_unwrapped_the_same_way_by_both()
        {
            var source = ASession();

            Assert.Equal(Auto.Map<SessionQuery.Result>(source).Id, new DddMappers().ToView(source).Id);
            Assert.Equal(Auto.Map<SessionQuery.Result>(source).SpeakerId,
                new DddMappers().ToView(source).SpeakerId);
        }

        [Fact]
        public void An_id_that_was_NOT_configured_would_be_a_build_error_here_and_a_default_there()
        {
            // The argument for compile-time mapping, made concrete rather than asserted. AutoMapper needs a
            // ForMember per strongly-typed id exactly as DwarfMapper needs a [MapProperty] — identical cost. The
            // difference is what happens when one is MISSING: a default Guid that reaches the wire, versus a
            // build that stops. In a codebase with a hundred ids that is the whole argument.
            Assert.NotEqual(Guid.Empty, new DddMappers().ToView(ASession()).Id);
        }

        [Fact]
        public void A_value_object_flattened_to_two_members_agrees()
        {
            var source = ASession();

            var expected = Auto.Map<SessionQuery.Result>(source);
            var actual = new DddMappers().ToView(source);

            Assert.Equal(expected.StartsAt, actual.StartsAt);
            Assert.Equal(expected.EndsAt, actual.EndsAt);
            Assert.Equal(Start, actual.StartsAt);
        }

        [Fact]
        public void A_value_object_rendered_by_a_converter_agrees()
        {
            var source = ASession();

            Assert.Equal(Auto.Map<SessionQuery.Result>(source).TicketPrice,
                new DddMappers().ToView(source).TicketPrice);
            Assert.Equal("129.50 GBP", new DddMappers().ToView(source).TicketPrice);
        }

        [Fact]
        public void A_flags_enum_as_text_agrees()
        {
            // A combined flags value formats as a comma-joined list, which is the case a naive enum-to-string
            // switch gets wrong — it recognises single members only.
            var source = ASession();

            Assert.Equal(Auto.Map<SessionQuery.Result>(source).Channels,
                new DddMappers().ToView(source).Channels);
            Assert.Contains("Livestream", new DddMappers().ToView(source).Channels, StringComparison.Ordinal);
        }

        [Fact]
        public void The_audited_base_members_reach_no_view_on_either_side()
        {
            // Four members on every entity that no view carries. AutoMapper says nothing about an unmapped
            // SOURCE member — convenient right up to the day one of them mattered. Asserted here so the silence
            // is a checked property rather than an assumption.
            var names = typeof(SessionQuery.Result).GetProperties().Select(p => p.Name).ToList();

            Assert.DoesNotContain(nameof(AuditedEntity.CreatedAt), names);
            Assert.DoesNotContain(nameof(AuditedEntity.CreatedBy), names);
            Assert.DoesNotContain(nameof(AuditedEntity.IsDeleted), names);
        }

        [Fact]
        public void A_positional_record_is_constructed_by_parameter_and_agrees()
        {
            var slot = new Slot("Hall A", new TimeWindow(Start, Start.AddMinutes(45)));

            var expected = Auto.Map<SessionQuery.SlotView>(slot);
            var actual = new DddMappers().ToSlotView(slot);

            Assert.Equal(expected.Room, actual.Room);
            Assert.Equal(expected.Start, actual.Start);
            Assert.Equal(expected.End, actual.End);
        }

        [Fact]
        public void The_read_only_child_collection_is_mapped_OUT_and_not_back()
        {
            // Session.Slots has no setter and is backed by a private list. Mapping OUT is ordinary. Mapping IN
            // would mean writing through the aggregate's back — AutoMapper does it reflectively and says nothing;
            // DwarfMapper cannot, so the reverse direction is simply not declared. That is the domain's decision
            // being honoured rather than bypassed, and it is the single biggest behavioural difference in this
            // corpus.
            var source = ASession();

            Assert.Single(source.Slots);
            Assert.Single(Auto.Map<SessionQuery.Result>(source).Slots);

            // And the aggregate still refuses to be filled from outside: the only route in is Schedule().
            Assert.Null(typeof(Session).GetProperty(nameof(Session.Slots))!.SetMethod);
        }

        [Fact]
        public void The_dictionary_member_survives()
        {
            var source = ASession();
            source.LocalisedTitles["cs"] = "Mapování v době překladu";
            source.LocalisedTitles["en"] = "Compile-time mapping";

            var view = new DddMappers().ToView(source);

            Assert.Equal(2, view.LocalisedTitles.Count);
            Assert.Equal("Compile-time mapping", view.LocalisedTitles["en"]);
        }
    }
}
