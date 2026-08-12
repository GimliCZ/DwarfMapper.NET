// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using AutoMapper;
using CleanCorpus.Ddd;
using DwarfMapper;

namespace CleanCorpus.Ddd;

// ═══ The view models ═════════════════════════════════════════════════════════════════════════════════════
// Records, nested inside the handler that owns them — the ContosoUniversity shape, which is how AutoMapper's
// own author writes them. A mapper has to cope with positional constructors and with types that are not
// top-level.

public static class SessionQuery
{
    public sealed record Result
    {
        public Guid Id { get; init; }

        public string Title { get; init; } = "";

        public Guid SpeakerId { get; init; }

        /// <summary>Flattened from the <c>TimeWindow</c> value object.</summary>
        public DateTimeOffset StartsAt { get; init; }

        public DateTimeOffset EndsAt { get; init; }

        /// <summary>A value object rendered, not copied — the converter case.</summary>
        public string TicketPrice { get; init; } = "";

        /// <summary>A flags enum as text.</summary>
        public string Channels { get; init; } = "";

        public IReadOnlyList<SlotView> Slots { get; init; } = [];

        public IReadOnlyDictionary<string, string> LocalisedTitles { get; init; } =
            new Dictionary<string, string>();
    }

    /// <summary>A positional record: construction, not member assignment.</summary>
    public sealed record SlotView(string Room, DateTimeOffset Start, DateTimeOffset End);
}

// ═══ BEFORE — AutoMapper ═════════════════════════════════════════════════════════════════════════════════

public sealed class DddProfile : Profile
{
    public DddProfile()
    {
        // Every one of these is a member AutoMapper cannot convention-match: a strongly-typed id to a Guid, a
        // value object to two members, another value object to a formatted string, a flags enum to text.
        // Note what is NOT here — nothing mentions the four AuditedEntity members. AutoMapper ignores an
        // unmapped SOURCE member in silence, which is convenient right up to the day one of them mattered.
        CreateMap<Session, SessionQuery.Result>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.Id.Value))
            .ForMember(d => d.SpeakerId, o => o.MapFrom(s => s.Speaker.Value))
            .ForMember(d => d.StartsAt, o => o.MapFrom(s => s.Window.Start))
            .ForMember(d => d.EndsAt, o => o.MapFrom(s => s.Window.End))
            .ForMember(d => d.TicketPrice, o => o.MapFrom(s => Rendering.Price(s.TicketPrice)))
            .ForMember(d => d.Channels, o => o.MapFrom(s => s.Channels.ToString()));

        CreateMap<Slot, SessionQuery.SlotView>()
            .ForCtorParam("Start", o => o.MapFrom(s => s.Window.Start))
            .ForCtorParam("End", o => o.MapFrom(s => s.Window.End));
    }
}

/// <summary>Shared by both sides so the corpus compares MAPPERS, not two renderings of a price.</summary>
public static class Rendering
{
    public static string Price(Money money)
    {
        ArgumentNullException.ThrowIfNull(money);

        return string.Create(CultureInfo.InvariantCulture, $"{money.Amount:0.00} {money.Currency}");
    }
}

// ═══ AFTER — DwarfMapper ═════════════════════════════════════════════════════════════════════════════════

/// <summary>
///     The same view, converted.
/// </summary>
/// <remarks>
///     <para>
///         CONVERSION NOTE 5 — <b>strongly-typed ids need a converter each way, and that is a feature.</b>
///         AutoMapper needed a <c>ForMember</c> per id too, so the cost is identical; what differs is that a
///         MISSING one is a build error here and a silently-default member there. In a codebase with a
///         hundred ids that difference is the whole argument.
///     </para>
///     <para>
///         CONVERSION NOTE 6 — <b>the read-only child collection is the shape that actually bites.</b>
///         <c>Session.Slots</c> has no setter and is backed by a private list; AutoMapper writes into the
///         backing field reflectively and never says so. DwarfMapper cannot, and will not pretend to — so
///         only the entity→view direction is declared. The reverse would need the aggregate's own
///         <c>Schedule</c> method, which is exactly what the domain intended and what a mapper should not be
///         quietly bypassing.
///     </para>
/// </remarks>
[DwarfMapper]
public partial class DddMappers
{
    [MapProperty(nameof(Session.Id) + "." + nameof(SessionId.Value), nameof(SessionQuery.Result.Id))]
    [MapProperty(nameof(Session.Speaker) + "." + nameof(SpeakerId.Value),
        nameof(SessionQuery.Result.SpeakerId))]
    [MapProperty(nameof(Session.Window) + "." + nameof(TimeWindow.Start),
        nameof(SessionQuery.Result.StartsAt))]
    [MapProperty(nameof(Session.Window) + "." + nameof(TimeWindow.End), nameof(SessionQuery.Result.EndsAt))]
    [MapProperty(nameof(Session.TicketPrice), nameof(SessionQuery.Result.TicketPrice),
        Use = nameof(RenderPrice))]
    [MapProperty(nameof(Session.Channels), nameof(SessionQuery.Result.Channels))]
    public partial SessionQuery.Result ToView(Session source);

    /// <summary>
    ///     The positional record — construction, not member assignment.
    /// </summary>
    /// <remarks>
    ///     CONVERSION NOTE 7. AutoMapper's <c>ForCtorParam("Start", o =&gt; o.MapFrom(s =&gt; s.Window.Start))</c>
    ///     is one <c>[MapProperty]</c> per parameter — the same dotted path the two members above use, aimed at
    ///     a constructor parameter instead of a property.
    ///     <para>
    ///         It did not translate when this corpus was written: the path resolved for a member target and was
    ///         refused for a parameter, under a <c>DWARF009</c> that claimed the member did not exist. That was
    ///         R18-31, found here and fixed; the workaround it forced was a converter per parameter taking the
    ///         value object whole. This is what it should have been in the first place.
    ///     </para>
    /// </remarks>
    [MapProperty(nameof(Slot.Window) + "." + nameof(TimeWindow.Start), "Start")]
    [MapProperty(nameof(Slot.Window) + "." + nameof(TimeWindow.End), "End")]
    public partial SessionQuery.SlotView ToSlotView(Slot source);

    private static string RenderPrice(Money money) => Rendering.Price(money);
}
