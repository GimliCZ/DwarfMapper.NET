// SPDX-License-Identifier: GPL-2.0-only

using System.ComponentModel;
using Riok.Mapperly.Abstractions;

namespace DwarfMapper.DifferentialTests;

// ═══ The shapes ══════════════════════════════════════════════════════════════════════════════════════════
// Ordinary DTO shapes, written the way DTOs actually get written rather than the way a generator author would
// choose to test one. Each is mapped by all three mappers from the SAME payload and the results compared.

// ── S1 flat scalars, including the conversions everyone gets slightly differently ─────────────────────────
public class FlatSrc
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public Guid Reference { get; set; }

    public DateTime CreatedAt { get; set; }

    public decimal Total { get; set; }

    public bool Active { get; set; }
}

public class FlatDst
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public Guid Reference { get; set; }

    public DateTime CreatedAt { get; set; }

    public decimal Total { get; set; }

    public bool Active { get; set; }
}

// ── S2 nested object, one level ───────────────────────────────────────────────────────────────────────────
public class Address
{
    public string City { get; set; } = "";

    public string Postcode { get; set; } = "";
}

public class AddressDto
{
    public string City { get; set; } = "";

    public string Postcode { get; set; } = "";
}

public class PersonSrc
{
    public string Name { get; set; } = "";

    public Address Home { get; set; } = new();
}

public class PersonDst
{
    public string Name { get; set; } = "";

    public AddressDto Home { get; set; } = new();
}

// ── S3 collections: a list of nested elements, an array of scalars, a dictionary ──────────────────────────
public class BasketSrc
{
    public List<Address> Stops { get; set; } = [];

    public string[] Tags { get; set; } = [];

    public Dictionary<string, int> Counts { get; set; } = [];
}

public class BasketDst
{
    public List<AddressDto> Stops { get; set; } = [];

    public string[] Tags { get; set; } = [];

    public Dictionary<string, int> Counts { get; set; } = [];
}

// ── S4 nullable members ───────────────────────────────────────────────────────────────────────────────────
public class NullableSrc
{
    public string? Note { get; set; }

    public int? Count { get; set; }

    public Address? Home { get; set; }
}

public class NullableDst
{
    public string? Note { get; set; }

    public int? Count { get; set; }

    public AddressDto? Home { get; set; }
}

// ── S5 a record target with a constructor ─────────────────────────────────────────────────────────────────
public class OrderSrc
{
    public int Number { get; set; }

    public string Customer { get; set; } = "";
}

public record OrderDto(int Number, string Customer);

// ── S6 enum to string — the shape chosen BECAUSE the three disagree ───────────────────────────────────────
// [Description] is a display annotation to most people and a persistence format to DwarfMapper's default.
// Included deliberately: an allowlist nobody ever exercises is decoration, and this proves the mechanism.
public enum Status
{
    [Description("in-progress")] InProgress,

    Done
}

public class StatusSrc
{
    public Status Status { get; set; }
}

public class StatusDst
{
    public string Status { get; set; } = "";
}

// ═══ DwarfMapper ═════════════════════════════════════════════════════════════════════════════════════════
[DwarfMapper]
[GenerateMap<FlatSrc, FlatDst>]
[GenerateMap<Address, AddressDto>]
[GenerateMap<PersonSrc, PersonDst>]
[GenerateMap<BasketSrc, BasketDst>]
[GenerateMap<NullableSrc, NullableDst>]
[GenerateMap<OrderSrc, OrderDto>]
[GenerateMap<StatusSrc, StatusDst>]
[GenerateMap<OrderedSrc, OrderedDst>]
public partial class DwarfShapes;

/// <summary>The same enum shape under the parity switch, which is what Mapperly and AutoMapper both do.</summary>
[DwarfMapper(EnumStringSource = EnumStringSource.Identifier)]
[GenerateMap<StatusSrc, StatusDst>]
public partial class DwarfShapesIdentifierEnums;

// ═══ Mapperly ════════════════════════════════════════════════════════════════════════════════════════════
[Mapper]
public partial class MapperlyShapes
{
    public partial FlatDst ToFlat(FlatSrc src);

    public partial AddressDto ToAddress(Address src);

    public partial PersonDst ToPerson(PersonSrc src);

    public partial BasketDst ToBasket(BasketSrc src);

    public partial NullableDst ToNullable(NullableSrc src);

    public partial OrderDto ToOrder(OrderSrc src);

    public partial StatusDst ToStatus(StatusSrc src);

    public partial OrderedDst ToOrdered(OrderedSrc src);
}

// ── S7 reserved-keyword member names — HARVESTED, AND IT DOES NOT COMPILE ────────────────────────────────
// Deliberately left commented out rather than deleted. A DTO with a member called `@class` or `@event` is
// ordinary in code generated from a schema, and this shape found that BOTH DwarfMapper and Mapperly emit the
// name unescaped: `class = src.class;`, which the C# compiler parses as a malformed event declaration. The
// harvest's first genuine defect, in two generators at once. Task R18-30; uncomment when the emitters escape.
//
// public class KeywordSrc { public string @class { get; set; } = ""; public int @event { get; set; } }
// public class KeywordDst { public string @class { get; set; } = ""; public int @event { get; set; } }

// ── S8 Stack and Queue — where element ORDER is the whole question ────────────────────────────────────────
// Harvested shape (R18-29): both types appeared in the corpus; whether the order survives was never asserted.
// It is a real hazard — enumerating a Stack yields last-in-first-out, so a mapper that rebuilds one by
// pushing in enumeration order reverses it, silently, and only for that one collection kind.
public class OrderedSrc
{
    public Stack<int> Recent { get; set; } = new();

    public Queue<string> Pending { get; set; } = new();
}

public class OrderedDst
{
    public Stack<int> Recent { get; set; } = new();

    public Queue<string> Pending { get; set; } = new();
}
