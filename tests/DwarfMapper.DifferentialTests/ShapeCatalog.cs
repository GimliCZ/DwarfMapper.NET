// SPDX-License-Identifier: GPL-2.0-only

using AutoMapper;

namespace DwarfMapper.DifferentialTests;

/// <summary>One shape mapped by DwarfMapper and by one oracle, ready to compare.</summary>
internal sealed record Comparison(string Shape, string Oracle, object? Dwarf, object? Oracle_);

/// <summary>
///     Every (shape, oracle) comparison this project makes, in one enumerable.
/// </summary>
/// <remarks>
///     <para>
///         A catalogue rather than a list of test methods, because two different tests need the same set: the
///         per-shape agreement assertions, and the ledger check that every accepted divergence is still real.
///         The first draft had the ledger read a side-channel of "which entries got hit", which quietly
///         depended on xUnit running the two test classes in a particular order — it does not, and the check
///         failed against an allowlist that was in fact being exercised. Deriving both from one enumerable
///         removes the ordering assumption instead of pinning it down.
///     </para>
///     <para>
///         Payloads are fixed rather than random. The fuzzer already covers distribution; a differential
///         harness that fails intermittently just teaches people to re-run it.
///     </para>
/// </remarks>
internal static class ShapeCatalog
{
    private static readonly Guid Reference = new("2f1c8a44-9d3e-4b21-8e6f-1a7c5d0b93e2");

    private static readonly DateTime Created = new(2026, 8, 12, 9, 30, 0, DateTimeKind.Utc);

    private static readonly DwarfShapes Dwarf = new();

    private static readonly DwarfShapesIdentifierEnums DwarfIdentifierEnums = new();

    private static readonly MapperlyShapes Mapperly = new();

    private static readonly IMapper Auto = new MapperConfiguration(c =>
    {
        c.CreateMap<FlatSrc, FlatDst>();
        c.CreateMap<Address, AddressDto>();
        c.CreateMap<PersonSrc, PersonDst>();
        c.CreateMap<BasketSrc, BasketDst>();
        c.CreateMap<NullableSrc, NullableDst>();
        c.CreateMap<OrderSrc, OrderDto>();
        c.CreateMap<StatusSrc, StatusDst>();
        c.CreateMap<OrderedSrc, OrderedDst>();
    }).CreateMapper();

    public static IEnumerable<Comparison> All()
    {
        var flat = new FlatSrc
        {
            Id = 42, Name = "Balin", Reference = Reference, CreatedAt = Created,
            Total = 1234.56m, Active = true
        };
        yield return new Comparison("FlatScalars", Oracles.Mapperly, Dwarf.Map(flat), Mapperly.ToFlat(flat));
        yield return new Comparison("FlatScalars", Oracles.AutoMapper, Dwarf.Map(flat), Auto.Map<FlatDst>(flat));

        // Address -> AddressDto on its own, not only as somebody's nested member. Added because the
        // coverage ratchet asked for it on its first run: the pair was declared on all three mappers and
        // reached only through PersonSrc and BasketSrc, so a divergence in the pair ITSELF would have been
        // visible exclusively through whichever container happened to hold it.
        var address = new Address { City = "Erebor", Postcode = "LM1" };
        yield return new Comparison("FlatNestedTypeAlone", Oracles.Mapperly,
            Dwarf.Map(address), Mapperly.ToAddress(address));
        yield return new Comparison("FlatNestedTypeAlone", Oracles.AutoMapper,
            Dwarf.Map(address), Auto.Map<AddressDto>(address));

        var person = new PersonSrc { Name = "Dwalin", Home = new Address { City = "Erebor", Postcode = "LM1" } };
        yield return new Comparison("NestedObject", Oracles.Mapperly, Dwarf.Map(person), Mapperly.ToPerson(person));
        yield return new Comparison("NestedObject", Oracles.AutoMapper,
            Dwarf.Map(person), Auto.Map<PersonDst>(person));

        var basket = new BasketSrc
        {
            Stops =
            [
                new Address { City = "Bree", Postcode = "B1" },
                new Address { City = "Rivendell", Postcode = "R2" }
            ],
            Tags = ["urgent", "fragile"],
            Counts = new Dictionary<string, int> { ["axes"] = 3, ["helms"] = 1 }
        };
        yield return new Comparison("Collections", Oracles.Mapperly, Dwarf.Map(basket), Mapperly.ToBasket(basket));
        yield return new Comparison("Collections", Oracles.AutoMapper,
            Dwarf.Map(basket), Auto.Map<BasketDst>(basket));

        // Separate from the populated case because empty-vs-null is a documented axis on which mappers
        // differ, and burying it inside a populated payload would mean never actually asking the question.
        var empty = new BasketSrc();
        yield return new Comparison("EmptyCollections", Oracles.Mapperly,
            Dwarf.Map(empty), Mapperly.ToBasket(empty));
        yield return new Comparison("EmptyCollections", Oracles.AutoMapper,
            Dwarf.Map(empty), Auto.Map<BasketDst>(empty));

        var filled = new NullableSrc
        {
            Note = "keep", Count = 7, Home = new Address { City = "Moria", Postcode = "M1" }
        };
        yield return new Comparison("NullableMembersWithValues", Oracles.Mapperly,
            Dwarf.Map(filled), Mapperly.ToNullable(filled));
        yield return new Comparison("NullableMembersWithValues", Oracles.AutoMapper,
            Dwarf.Map(filled), Auto.Map<NullableDst>(filled));

        // Does a null source member arrive as null, or as an empty/default value? Three mappers, one question.
        var nulls = new NullableSrc();
        yield return new Comparison("NullableMembersWhenNull", Oracles.Mapperly,
            Dwarf.Map(nulls), Mapperly.ToNullable(nulls));
        yield return new Comparison("NullableMembersWhenNull", Oracles.AutoMapper,
            Dwarf.Map(nulls), Auto.Map<NullableDst>(nulls));

        var order = new OrderSrc { Number = 9, Customer = "Gloin" };
        yield return new Comparison("RecordWithConstructor", Oracles.Mapperly,
            Dwarf.Map(order), Mapperly.ToOrder(order));
        yield return new Comparison("RecordWithConstructor", Oracles.AutoMapper,
            Dwarf.Map(order), Auto.Map<OrderDto>(order));

        // Chosen BECAUSE the three disagree — an allowlist nobody exercises is decoration, and a harness that
        // can only ever pass cannot be shown to work.
        var status = new StatusSrc { Status = Status.InProgress };
        yield return new Comparison("EnumToString", Oracles.Mapperly,
            Dwarf.Map(status), Mapperly.ToStatus(status));
        yield return new Comparison("EnumToString", Oracles.AutoMapper,
            Dwarf.Map(status), Auto.Map<StatusDst>(status));

        // A reserved-keyword shape (@class, @event) is NOT here, and that is a finding rather than an
        // omission: BOTH DwarfMapper and Mapperly emit the member name unescaped, so neither compiles. See
        // Issues/Rount18/ShapeInventory.md and task R18-30. The shape returns here when the emitters do.

        // Order is the whole question here: enumerating a Stack yields last-in-first-out, so a mapper that
        // rebuilds one by pushing in enumeration order reverses it — silently, and only for that one kind.
        var ordered = new OrderedSrc
        {
            Recent = new Stack<int>([1, 2, 3]),
            Pending = new Queue<string>(["first", "second"])
        };
        yield return new Comparison("StackAndQueueOrder", Oracles.Mapperly,
            Dwarf.Map(ordered), Mapperly.ToOrdered(ordered));
        yield return new Comparison("StackAndQueueOrder", Oracles.AutoMapper,
            Dwarf.Map(ordered), Auto.Map<OrderedDst>(ordered));

        // The other half of the same finding, and the reason the divergence above is a decision rather than a
        // defect: EnumStringSource.Identifier IS Mapperly's and AutoMapper's behaviour, in one line.
        yield return new Comparison("EnumToStringIdentifier", Oracles.Mapperly,
            DwarfIdentifierEnums.Map(status), Mapperly.ToStatus(status));
        yield return new Comparison("EnumToStringIdentifier", Oracles.AutoMapper,
            DwarfIdentifierEnums.Map(status), Auto.Map<StatusDst>(status));
    }
}
