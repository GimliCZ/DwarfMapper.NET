// SPDX-License-Identifier: GPL-2.0-only

using System.ComponentModel;

namespace CleanCorpus.Domain;

// ═══ The domain ══════════════════════════════════════════════════════════════════════════════════════════
// A library lending service, at the size and shape a CLEAN-architecture application actually has: four
// aggregates, entities with behaviour and non-public construction, value objects, an enum with a display
// annotation, and a polymorphic hierarchy reached through a collection.
//
// Original — it shares no entity, member or vocabulary with any of the projects whose PATTERNS it exercises.
// See PATTERN-PROVENANCE.md.

// ── Branch ───────────────────────────────────────────────────────────────────────────────────────────────

public sealed class Branch
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public PostalAddress Address { get; set; } = new();

    public List<Shelf> Shelves { get; set; } = [];
}

public sealed class PostalAddress
{
    public string Line1 { get; set; } = "";

    public string? Line2 { get; set; }

    public string Town { get; set; } = "";

    public string Postcode { get; set; } = "";
}

public sealed class Shelf
{
    public string Code { get; set; } = "";

    public int Capacity { get; set; }
}

// ── Catalogue ────────────────────────────────────────────────────────────────────────────────────────────

public enum Availability
{
    [Description("on-shelf")] OnShelf,

    [Description("on-loan")] OnLoan,

    Withdrawn
}

public enum Format
{
    Hardback,

    Paperback,

    Audio
}

/// <summary>
///     The base of a small hierarchy reached through <see cref="Branch" />-scoped collections. A collection
///     loop binds at COMPILE time, so a derived item inside a base-typed list is where a mapper's dispatch
///     model becomes visible.
/// </summary>
public class CatalogueItem
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public Availability Availability { get; set; }

    public Format Format { get; set; }

    /// <summary>Flattened by convention on the AutoMapper side — <c>Publisher.Name</c> → <c>PublisherName</c>.</summary>
    public Publisher Publisher { get; set; } = new();
}

public sealed class AudioBook : CatalogueItem
{
    public int RuntimeMinutes { get; set; }

    public string Narrator { get; set; } = "";
}

public sealed class Publisher
{
    public string Name { get; set; } = "";

    public string? Imprint { get; set; }
}

// ── Membership ───────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
///     Constructed only through <see cref="Register" />, and holds an <c>init</c>-only card number. This is
///     the shape a reflective mapper bypasses without anyone noticing, and the one <c>DWARF080</c> reports.
/// </summary>
public sealed class Member
{
    /// <summary>
    ///     <b>internal</b>, and that is the conversion's one domain change. It was <c>private</c>, which
    ///     AutoMapper reached by reflection and DwarfMapper refuses to (<c>DWARF026</c>) — it generates
    ///     ordinary C#, so a private constructor is genuinely unreachable. Widening it is the compile-time,
    ///     compiler-checked version of the grant that was being taken anyway.
    /// </summary>
    internal Member(string displayName) => DisplayName = displayName;

    public string DisplayName { get; }

    public string CardNumber { get; init; } = "";

    public string? EmailAddress { get; set; }

    public DateOnly JoinedOn { get; set; }

    public static Member Register(string displayName) => new(displayName);
}

// ── Lending ──────────────────────────────────────────────────────────────────────────────────────────────

public sealed class Loan
{
    public int Id { get; set; }

    public CatalogueItem Item { get; set; } = new();

    public Member Borrower { get; set; } = Member.Register("");

    public DateOnly TakenOn { get; set; }

    public DateOnly? DueOn { get; set; }

    /// <summary>Set only once a loan is closed; a condition-guarded member on the AutoMapper side.</summary>
    public DateOnly? ReturnedOn { get; set; }

    public decimal AccruedFine { get; set; }
}

// ── A generic wrapper, because every one of these applications has one ───────────────────────────────────

public sealed class Page<T>
{
    public List<T> Items { get; set; } = [];

    public int PageNumber { get; set; }

    public int TotalPages { get; set; }
}
