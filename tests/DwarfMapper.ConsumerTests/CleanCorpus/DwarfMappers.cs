// SPDX-License-Identifier: GPL-2.0-only

using CleanCorpus.Application;
using CleanCorpus.Domain;
using DwarfMapper;

namespace CleanCorpus.After;

// ═══ The AFTER side ══════════════════════════════════════════════════════════════════════════════════════
// The same corpus, converted. Every difference from CorpusProfile is deliberate and recorded in
// CONVERSION-NOTES.md; nothing here is a workaround for something the generator could not express.

/// <summary>
///     Branch and its shelves.
/// </summary>
/// <remarks>
///     CONVERSION NOTE 1. <c>[Flatten]</c> was the obvious translation and it is the WRONG one. AutoMapper's
///     convention prefixes the flattened member with the containing member's NAME —
///     <c>Address.Town</c> becomes <c>AddressTown</c> — while <c>[Flatten]</c> lifts the leaf under its own
///     name, producing <c>Town</c>. The pair then fails the completeness gate on two members that look like
///     they should have matched. Stated per member instead, which is longer and says what it does.
/// </remarks>
[DwarfMapper]
[GenerateMap<Shelf, ShelfDto>]
[MapProperty<Shelf, ShelfDto>(nameof(Shelf.Code), nameof(ShelfDto.Reference))]
public partial class BranchMappers
{
    [MapProperty(nameof(Branch.Address) + "." + nameof(PostalAddress.Town), nameof(BranchDto.AddressTown))]
    [MapProperty(nameof(Branch.Address) + "." + nameof(PostalAddress.Postcode),
        nameof(BranchDto.AddressPostcode))]
    public partial BranchDto ToDto(Branch source);

    /// <summary>The reverse of the shelf pair — AutoMapper's <c>ReverseMap()</c>, said once per direction.</summary>
    [MapProperty(nameof(ShelfDto.Reference), nameof(Shelf.Code))]
    public partial Shelf ToShelf(ShelfDto source);
}

/// <summary>
///     The catalogue, including the polymorphic arm.
/// </summary>
/// <remarks>
///     <para>
///         The enum→<c>int</c> cast AutoMapper needed is gone: DwarfMapper converts an enum to a numeric
///         target natively, so the configuration for it is nothing at all.
///     </para>
///     <para>
///         The enum→<c>string</c> member is where the two mappers genuinely differ.
///         <c>Format</c> has no annotations so both write the identifier, but <c>Availability</c> carries
///         <c>[Description("on-shelf")]</c> — and <c>DWARF083</c> exists to make sure nobody discovers that
///         the way the Round-18 consumer nearly did.
///     </para>
/// </remarks>
[DwarfMapper]
public partial class CatalogueMappers
{
    /// <summary>
    ///     The base pair. <c>[MapDerivedType]</c> restores the RUNTIME dispatch AutoMapper had — without it a
    ///     collection loop binds this pair at compile time and an audio book inside a
    ///     <c>List&lt;CatalogueItem&gt;</c> silently arrives with no narrator.
    /// </summary>
    [MapDerivedType<AudioBook, AudioBookDto>]
    [MapDerivedType<CatalogueItem, CatalogueItemDto>]
    public partial CatalogueItemDto ToDto(CatalogueItem source);

    /// <summary>The base arm, mapping a plain item. Narrator has no source, so it is stated as ignored.</summary>
    [MapProperty(nameof(CatalogueItem.Publisher) + "." + nameof(Publisher.Name),
        nameof(CatalogueItemDto.PublisherName))]
    [MapIgnore(nameof(CatalogueItemDto.Narrator))]
    public partial CatalogueItemDto ToBaseDto(CatalogueItem source);

    /// <summary>
    ///     The derived arm. AutoMapper wrote <c>IncludeBase</c>; here the base configuration is restated and
    ///     <c>[RestatesBase]</c> checks that the restatement has not drifted — which <c>IncludeBase</c> never
    ///     did.
    /// </summary>
    [MapProperty(nameof(CatalogueItem.Publisher) + "." + nameof(Publisher.Name),
        nameof(CatalogueItemDto.PublisherName))]
    public partial AudioBookDto ToAudioDto(AudioBook source);
}

/// <summary>
///     Membership. <c>Member</c> has a private constructor and an <c>init</c>-only card number.
/// </summary>
/// <remarks>
///     CONVERSION NOTE 2, and the one that needed a DOMAIN change. AutoMapper reached the private constructor
///     by reflection; DwarfMapper will not, so the pair was <c>DWARF026</c> — no accessible constructor — and
///     no attribute fixes that.
///     <para>
///         <c>[MapConstructor]</c> naming <c>Register</c> is the literal translation of <c>ConstructUsing</c>
///         and it compiles. It also loses <c>CardNumber</c>: a factory owns construction, so an <c>init</c>
///         member cannot be assigned afterwards, and <c>DWARF080</c> reports that at build time rather than
///         letting it ship. The conversion widened the constructor to <c>internal</c> instead and binds its
///         PARAMETER, which is what the diagnostic recommends and what keeps the member.
///     </para>
/// </remarks>
[DwarfMapper(AllowNonPublic = true)]
[GenerateMap<MemberDto, Member>]
[MapProperty<MemberDto, Member>(nameof(MemberDto.DisplayName), "displayName")]
public partial class MemberMappers
{
    /// <summary>A null email becomes the empty string — AutoMapper's <c>NullSubstitute</c>, verbatim.</summary>
    [MapProperty(nameof(Member.EmailAddress), nameof(MemberDto.EmailAddress), NullSubstitute = "")]
    public partial MemberDto ToDto(Member source);
}

/// <summary>
///     Lending — the pair with a guard and a computed member.
/// </summary>
[DwarfMapper]
public partial class LoanMappers
{
    [MapProperty(nameof(Loan.Item) + ".Title", nameof(LoanDto.ItemTitle))]
    [MapProperty(nameof(Loan.Borrower) + ".CardNumber", nameof(LoanDto.BorrowerCardNumber))]
    // AutoMapper's Condition — the member is written only when the loan is closed.
    [MapProperty(nameof(Loan.ReturnedOn), nameof(LoanDto.ReturnedOn), When = nameof(IsClosed))]
    // AutoMapper's resolver. Both sides call the same function, so the corpus compares mappers rather than
    // two implementations of a fine policy.
    [MapProperty(nameof(Loan.AccruedFine), nameof(LoanDto.FineDescription), Use = nameof(DescribeFine))]
    public partial LoanDto ToDto(Loan source);

    private static bool IsClosed(Loan source) => source.ReturnedOn.HasValue;

    private static string DescribeFine(decimal accrued) => Before.Fines.Describe(accrued);
}

/// <summary>
///     The paged wrapper, closed over the instantiation the application uses.
/// </summary>
/// <remarks>
///     CONVERSION NOTE 3. <c>[GenerateWrapperMap]</c> was the obvious translation and it is refused
///     (<c>DWARF067</c>): it expresses "map this generic shell for every declared payload pair" and requires a
///     SINGLE-payload generic, while <c>Page&lt;T&gt;</c> carries paging metadata alongside its items. So the
///     closed instantiation is declared, which is exactly what AutoMapper needed too — one <c>CreateMap</c>
///     per closed type. No ground lost, and the diagnostic said so at build time.
/// </remarks>
[DwarfMapper]
[GenerateMap<CatalogueItem, CatalogueItemDto>]
[MapProperty<CatalogueItem, CatalogueItemDto>(
    nameof(CatalogueItem.Publisher) + "." + nameof(Publisher.Name), nameof(CatalogueItemDto.PublisherName))]
[MapIgnore<CatalogueItemDto>(nameof(CatalogueItemDto.Narrator))]
[GenerateMap<Page<CatalogueItem>, PageDto<CatalogueItemDto>>]
public partial class PageMappers
{
}
