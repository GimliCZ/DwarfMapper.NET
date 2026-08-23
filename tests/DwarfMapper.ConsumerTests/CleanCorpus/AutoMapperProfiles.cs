// SPDX-License-Identifier: GPL-2.0-only

using AutoMapper;
using CleanCorpus.Application;
using CleanCorpus.Domain;

namespace CleanCorpus.Before
{
    // ═══ The BEFORE side ═════════════════════════════════════════════════════════════════════════════════════
// The corpus as an AutoMapper application has it. Two profile styles on purpose, because real codebases have
// both: a central profile holding several CreateMaps, and a DTO that declares its own mapping in a nested
// Profile class. See PATTERN-PROVENANCE.md for which project each idiom was harvested from.

    /// <summary>The central profile — the eShopOnWeb style.</summary>
    public sealed class CorpusProfile : Profile
    {
        public CorpusProfile()
        {
            // #1 convention-matched, plus #5 flattening: AddressTown and AddressPostcode come from Address.Town
            // and Address.Postcode with no configuration at all. Silent, and the reader has to know the rule.
            CreateMap<Branch, BranchDto>();

            // #2 a renamed member.
            CreateMap<Shelf, ShelfDto>()
                .ForMember(d => d.Reference, o => o.MapFrom(s => s.Code));

            // #3 enum -> int by cast, #4 enum -> string, #5 flattening, #9 an ignore for a member only the
            // derived type can fill.
            CreateMap<CatalogueItem, CatalogueItemDto>()
                .ForMember(d => d.Availability, o => o.MapFrom(s => (int)s.Availability))
                .ForMember(d => d.Format, o => o.MapFrom(s => s.Format.ToString()))
                .ForMember(d => d.Narrator, o => o.Ignore());

            // #14 the polymorphic arm. AutoMapper dispatches on the RUNTIME type, so an AudioBook inside a
            // List<CatalogueItem> maps through here and keeps its Narrator.
            CreateMap<AudioBook, AudioBookDto>()
                .IncludeBase<CatalogueItem, CatalogueItemDto>()
                .ForMember(d => d.Narrator, o => o.MapFrom(s => s.Narrator));

            // #7 ConstructUsing + #8 a non-public constructor: AutoMapper reaches Member's private constructor
            // reflectively; here it is told to use the factory instead. #10 NullSubstitute for the email.
            CreateMap<MemberDto, Member>()
                .ConstructUsing(s => Member.Register(s.DisplayName));
            CreateMap<Member, MemberDto>()
                .ForMember(d => d.EmailAddress, o => o.NullSubstitute(""));

            // #11 Condition, #12 a resolver expressed inline, #2 two renames through a nested path.
            CreateMap<Loan, LoanDto>()
                .ForMember(d => d.ItemTitle, o => o.MapFrom(s => s.Item.Title))
                .ForMember(d => d.BorrowerCardNumber, o => o.MapFrom(s => s.Borrower.CardNumber))
                .ForMember(d => d.ReturnedOn, o => o.Condition(s => s.ReturnedOn.HasValue))
                .ForMember(d => d.FineDescription, o => o.MapFrom(s => Fines.Describe(s.AccruedFine)));

            // #16 the generic wrapper, closed over the one instantiation the application uses.
            CreateMap<Page<CatalogueItem>, PageDto<CatalogueItemDto>>();
        }
    }

    /// <summary>
    ///     The other style: a DTO that declares its own mapping in a nested profile — the jasontaylordev pattern.
    /// </summary>
    /// <remarks>
    ///     Kept separate because it is the pattern, not because the pair needs it. #6 <c>ReverseMap</c> lives
    ///     here too, since round-tripping a shelf is the one place this corpus wants both directions from a
    ///     single declaration.
    /// </remarks>
    public sealed class ShelfRoundTripProfile : Profile
    {
        public ShelfRoundTripProfile()
        {
            CreateMap<ShelfDto, Shelf>()
                .ForMember(d => d.Code, o => o.MapFrom(s => s.Reference))
                .ReverseMap()
                .ForMember(d => d.Reference, o => o.MapFrom(s => s.Code));
        }
    }

    /// <summary>#12 — the value the resolver computes, kept as a plain function so both sides can share it.</summary>
    public static class Fines
    {
        public static string Describe(decimal accrued)
        {
            return accrued switch
            {
                <= 0m => "none",
                < 5m => "minor",
                _ => "escalated"
            };
        }
    }
}
