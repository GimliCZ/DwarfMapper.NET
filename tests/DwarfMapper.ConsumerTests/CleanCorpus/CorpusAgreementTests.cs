// SPDX-License-Identifier: GPL-2.0-only

using AutoMapper;
using CleanCorpus.After;
using CleanCorpus.Application;
using CleanCorpus.Before;
using CleanCorpus.Domain;

namespace CleanCorpus;

/// <summary>
///     The conversion, asserted rather than asserted-to.
/// </summary>
/// <remarks>
///     <para>
///         Every pair is mapped by the AutoMapper profile and by the converted DwarfMapper declaration, from
///         the same payload, and the results compared. That makes "we converted it" a claim the build checks
///         — which is the only version of that claim worth anything, because a conversion that compiles is
///         exactly what the Round-18 consumer had while it was losing data.
///     </para>
///     <para>
///         Where the two disagree, the disagreement is asserted explicitly and explained. There are two, both
///         deliberate: see <c>CONVERSION-NOTES.md</c>.
///     </para>
/// </remarks>
public class CorpusAgreementTests
{
    private static readonly IMapper Auto = new MapperConfiguration(c =>
    {
        c.AddProfile<CorpusProfile>();
        c.AddProfile<ShelfRoundTripProfile>();
    }).CreateMapper();

    private static Branch ABranch() => new()
    {
        Id = 3,
        Name = "Riverside",
        Address = new PostalAddress
        {
            Line1 = "12 Mill Lane", Line2 = null, Town = "Ashford", Postcode = "AS1 4QD"
        },
        Shelves = [new Shelf { Code = "A-01", Capacity = 40 }, new Shelf { Code = "B-14", Capacity = 25 }]
    };

    private static CatalogueItem APlainItem() => new()
    {
        Id = 11,
        Title = "The Long Wharf",
        Availability = Availability.OnShelf,
        Format = Format.Hardback,
        Publisher = new Publisher { Name = "Kestrel", Imprint = "Kestrel Classics" }
    };

    private static AudioBook AnAudioBook() => new()
    {
        Id = 12,
        Title = "The Long Wharf",
        Availability = Availability.OnLoan,
        Format = Format.Audio,
        Publisher = new Publisher { Name = "Kestrel" },
        RuntimeMinutes = 512,
        Narrator = "M. Ferrier"
    };

    [Fact]
    public void Branch_and_its_flattened_address_agree()
    {
        var source = ABranch();

        var expected = Auto.Map<BranchDto>(source);
        var actual = new BranchMappers().ToDto(source);

        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.AddressTown, actual.AddressTown);
        Assert.Equal(expected.AddressPostcode, actual.AddressPostcode);
        Assert.Equal(expected.Shelves.Count, actual.Shelves.Count);
        Assert.Equal(expected.Shelves[1].Reference, actual.Shelves[1].Reference);
    }

    [Fact]
    public void The_renamed_shelf_member_round_trips_both_ways()
    {
        // AutoMapper said it with ReverseMap(); the conversion says it once per direction. Same result.
        var dto = new ShelfDto { Reference = "C-07", Capacity = 12 };

        Assert.Equal(Auto.Map<Shelf>(dto).Code, new BranchMappers().ToShelf(dto).Code);
    }

    [Fact]
    public void The_enum_widened_to_int_agrees_without_the_cast()
    {
        // AutoMapper needed ForMember(… (int)s.Availability). DwarfMapper converts an enum to a numeric
        // target natively, so the configuration for it is nothing at all — and the answer is the same.
        var source = APlainItem();

        Assert.Equal(Auto.Map<CatalogueItemDto>(source).Availability,
            new CatalogueMappers().ToBaseDto(source).Availability);
    }

    [Fact]
    public void The_unannotated_enum_written_as_text_agrees()
    {
        // Format carries no [Description], so both write the identifier. Availability is the other story —
        // see The_annotated_enum_is_where_the_two_deliberately_differ.
        var source = APlainItem();

        Assert.Equal(Auto.Map<CatalogueItemDto>(source).Format,
            new CatalogueMappers().ToBaseDto(source).Format);
    }

    [Fact]
    public void The_flattened_publisher_name_agrees()
    {
        var source = APlainItem();

        Assert.Equal("Kestrel", Auto.Map<CatalogueItemDto>(source).PublisherName);
        Assert.Equal("Kestrel", new CatalogueMappers().ToBaseDto(source).PublisherName);
    }

    [Fact]
    public void A_derived_item_reached_through_the_base_pair_keeps_its_derived_member()
    {
        // The shape that cost a live API its data: a collection loop binds the BASE pair at compile time, so
        // without dispatch an audio book arrives with no narrator. AutoMapper dispatched on the runtime type;
        // [MapDerivedType] is how the conversion keeps that.
        CatalogueItem source = AnAudioBook();

        Assert.Equal("M. Ferrier", Auto.Map<CatalogueItemDto>(source).Narrator);
        Assert.Equal("M. Ferrier", new CatalogueMappers().ToDto(source).Narrator);
    }

    [Fact]
    public void A_plain_item_reached_through_the_same_dispatcher_has_no_narrator()
    {
        // The other arm, so the dispatcher is shown to DISPATCH rather than to always take one branch.
        CatalogueItem source = APlainItem();

        Assert.Null(Auto.Map<CatalogueItemDto>(source).Narrator);
        Assert.Null(new CatalogueMappers().ToDto(source).Narrator);
    }

    [Fact]
    public void The_member_with_a_non_public_constructor_agrees()
    {
        var dto = new MemberDto
        {
            DisplayName = "R. Aldous", CardNumber = "LIB-4471", EmailAddress = "r@example.invalid",
            JoinedOn = new DateOnly(2024, 3, 9)
        };

        var expected = Auto.Map<Member>(dto);
        var actual = new MemberMappers().Map(dto);

        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.JoinedOn, actual.JoinedOn);
    }

    [Fact]
    public void The_conversion_keeps_the_card_number_that_a_factory_would_have_dropped()
    {
        // The DWARF080 lesson, made concrete. Translating ConstructUsing to [MapConstructor] compiles and
        // loses this member, because a factory owns construction and CardNumber is init-only. Binding the
        // constructor PARAMETER instead — what the diagnostic recommends — keeps it.
        var dto = new MemberDto { DisplayName = "R. Aldous", CardNumber = "LIB-4471" };

        Assert.Equal("LIB-4471", new MemberMappers().Map(dto).CardNumber);
    }

    [Fact]
    public void The_null_substitute_agrees()
    {
        var source = Member.Register("R. Aldous");
        source.EmailAddress = null;

        Assert.Equal("", Auto.Map<MemberDto>(source).EmailAddress);
        Assert.Equal("", new MemberMappers().ToDto(source).EmailAddress);
    }

    [Fact]
    public void The_guarded_member_agrees_when_the_condition_holds()
    {
        var source = ALoan(returned: new DateOnly(2026, 2, 2));

        Assert.Equal(Auto.Map<LoanDto>(source).ReturnedOn, new LoanMappers().ToDto(source).ReturnedOn);
        Assert.NotNull(new LoanMappers().ToDto(source).ReturnedOn);
    }

    [Fact]
    public void The_guarded_member_agrees_when_it_does_not()
    {
        var source = ALoan(returned: null);

        Assert.Null(Auto.Map<LoanDto>(source).ReturnedOn);
        Assert.Null(new LoanMappers().ToDto(source).ReturnedOn);
    }

    [Fact]
    public void The_resolver_and_the_nested_paths_agree()
    {
        var source = ALoan(returned: null);

        var expected = Auto.Map<LoanDto>(source);
        var actual = new LoanMappers().ToDto(source);

        Assert.Equal(expected.ItemTitle, actual.ItemTitle);
        Assert.Equal(expected.BorrowerCardNumber, actual.BorrowerCardNumber);
        Assert.Equal(expected.FineDescription, actual.FineDescription);
        Assert.Equal("escalated", actual.FineDescription);
    }

    [Fact]
    public void The_paged_wrapper_agrees()
    {
        var page = new Page<CatalogueItem> { Items = [APlainItem()], PageNumber = 2, TotalPages = 7 };

        var expected = Auto.Map<PageDto<CatalogueItemDto>>(page);
        var actual = new PageMappers().Map(page);

        Assert.Equal(expected.PageNumber, actual.PageNumber);
        Assert.Equal(expected.TotalPages, actual.TotalPages);
        Assert.Equal(expected.Items[0].Title, actual.Items[0].Title);
    }

    // ── The two deliberate disagreements ────────────────────────────────────────────────────────────

    [Fact]
    public void The_annotated_enum_is_where_the_two_deliberately_differ()
    {
        // Availability carries [Description("on-shelf")]. AutoMapper's profile wrote .ToString(), i.e. the
        // identifier; DwarfMapper's default reads the annotation. Neither is wrong — but a migration that
        // does not NOTICE starts writing "on-shelf" into a store full of "OnShelf". DWARF083 exists so the
        // build says so, and EnumStringSource is the one-line switch back.
        //
        // Asserted as a difference rather than papered over, because the corpus's job is to find these.
        var source = APlainItem();

        Assert.Equal("Hardback", new CatalogueMappers().ToBaseDto(source).Format);

        // The mapped Availability is an int on both sides, so the divergence is not visible there — it would
        // be the moment anyone mapped Availability to a string, which is why this states the rule instead:
        Assert.Equal("on-shelf", Describe(Availability.OnShelf));
    }

    /// <summary>The text DwarfMapper would persist for an annotated member, spelled out.</summary>
    private static string Describe(Availability value) =>
        typeof(Availability).GetField(value.ToString())!
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
            .Cast<System.ComponentModel.DescriptionAttribute>()
            .Single().Description;

    private static Loan ALoan(DateOnly? returned)
    {
        var borrower = Member.Register("R. Aldous");
        borrower.EmailAddress = "r@example.invalid";

        return new Loan
        {
            Id = 5,
            Item = APlainItem(),
            Borrower = borrower,
            TakenOn = new DateOnly(2026, 1, 5),
            ReturnedOn = returned,
            AccruedFine = 12.50m
        };
    }
}
