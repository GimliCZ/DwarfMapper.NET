// SPDX-License-Identifier: GPL-2.0-only

namespace CleanCorpus.Ddd
{
    // ═══ The shapes a real DDD codebase actually has ══════════════════════════════════════════════════════════
// The first corpus in this directory covers the AutoMapper FEATURE list — rename, flatten, ignore, condition,
// resolver — which is the textbook set and, on its own, generic. This file covers what makes mapping hard in
// applications that are not textbooks: identity that is not a primitive, collections a mapper is not allowed
// to assign, construction that is closed by design, and a base class every entity carries and no DTO wants.
//
// Harvested (patterns only — see PATTERN-PROVENANCE.md):
//   * kgrzybek/modular-monolith-with-ddd — strongly-typed ids, private readonly backing lists exposed as
//     read-only, a private parameterless constructor for ORM hydration plus an internal factory, and value
//     objects held as fields rather than exposed as properties.
//   * jbogard/ContosoUniversityDotNetCore-Pages — records as view models NESTED inside their handler, and
//     projection-only maps over two levels of navigation.
//   * ABP-style frameworks — an audited base class on every entity whose members no DTO carries.

// ── Identity that is not a primitive ─────────────────────────────────────────────────────────────────────

/// <summary>
///     A strongly-typed id. Ubiquitous in DDD and a genuine mapping problem: the DTO carries a
///     <see cref="Guid" />, so every id member needs a conversion in each direction, and there are a lot of
///     them.
/// </summary>
public readonly record struct SessionId(Guid Value)
    {
        public static SessionId New()
        {
            return new SessionId(Guid.NewGuid());
        }
    }

    public readonly record struct SpeakerId(Guid Value);

// ── Value objects ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A half-open interval. Flattened to two DTO members, which is the usual treatment.</summary>
    public sealed record TimeWindow(DateTimeOffset Start, DateTimeOffset End)
    {
        public TimeSpan Duration => End - Start;
    }

    /// <summary>Amount plus currency. The DTO wants a formatted string, so this needs a real converter.</summary>
    public sealed record Money(decimal Amount, string Currency);

// ── The audited base every entity carries and no DTO wants ───────────────────────────────────────────────

/// <summary>
///     Four members on every entity that must NOT reach a DTO. AutoMapper ignores them by saying nothing;
///     DwarfMapper's source-completeness gate can be told to demand that every source member is read, at
///     which point they have to be refused deliberately.
/// </summary>
public abstract class AuditedEntity
    {
        public DateTimeOffset CreatedAt { get; set; }

        public string CreatedBy { get; set; } = "";

        public DateTimeOffset? LastModifiedAt { get; set; }

        public bool IsDeleted { get; set; }
    }

// ── The aggregate ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A real flags enum, because permission- and channel-sets in production systems are.</summary>
    [Flags]
    public enum Channel
    {
        None = 0,

        InPerson = 1,

        Livestream = 2,

        Recording = 4
    }

    /// <summary>
    ///     A conference session: closed construction, a read-only child collection with no setter, a
    ///     strongly-typed id, two value objects and a flags enum.
    /// </summary>
    /// <remarks>
    ///     Every one of those is a deliberate domain decision, and every one of them is something a reflective
    ///     mapper walks straight through without asking. That is what makes this the interesting corpus.
    /// </remarks>
    public sealed class Session : AuditedEntity
    {
        private readonly List<Slot> _slots = [];

        /// <summary>For ORM hydration only — the shape a mapper must NOT be able to use by accident.</summary>
        private Session()
        {
        }

        private Session(SessionId id, string title, SpeakerId speaker, TimeWindow window, Money ticketPrice)
        {
            Id = id;
            Title = title;
            Speaker = speaker;
            Window = window;
            TicketPrice = ticketPrice;
        }

        public SessionId Id { get; private set; }

        public string Title { get; private set; } = "";

        public SpeakerId Speaker { get; private set; }

        public TimeWindow Window { get; private set; } = new(default, default);

        public Money TicketPrice { get; private set; } = new(0m, "GBP");

        public Channel Channels { get; private set; } = Channel.InPerson;

        /// <summary>
        ///     Read-only, backed by a private list. A mapper cannot assign this member at all — the collection
        ///     has to be built through <see cref="Schedule" />, or the DTO direction has to be the only one
        ///     mapped. AutoMapper writes into the backing field reflectively without mentioning it.
        /// </summary>
        public IReadOnlyCollection<Slot> Slots => _slots;

        /// <summary>Localised titles, keyed by culture — a dictionary member, which most corpora skip.</summary>
        public Dictionary<string, string> LocalisedTitles { get; private set; } = [];

        public static Session Announce(string title, SpeakerId speaker, TimeWindow window, Money price)
        {
            return new Session(SessionId.New(), title, speaker, window, price);
        }

        public void Schedule(Slot slot)
        {
            _slots.Add(slot);
        }

        public void Broadcast(Channel channels)
        {
            Channels |= channels;
        }
    }

    /// <summary>A child entity, reached only through its aggregate.</summary>
    public sealed class Slot
    {
        public Slot(string room, TimeWindow window)
        {
            Room = room;
            Window = window;
        }

        public string Room { get; }

        public TimeWindow Window { get; }
    }

    public sealed class Speaker : AuditedEntity
    {
        public SpeakerId Id { get; set; }

        public string DisplayName { get; set; } = "";

        public string? Biography { get; set; }
    }
}
