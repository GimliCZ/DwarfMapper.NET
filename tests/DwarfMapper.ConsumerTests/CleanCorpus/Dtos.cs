// SPDX-License-Identifier: GPL-2.0-only

namespace CleanCorpus.Application
{
    // ═══ The DTOs ════════════════════════════════════════════════════════════════════════════════════════════
// Written the way an application layer writes them: renamed members, a flattened sub-object, an enum widened
// to int for the wire, an enum narrowed to a string for storage, and a wrapper for paged results.

    public sealed class BranchDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        /// <summary>Flattened from <c>Address.Town</c> — AutoMapper does this by convention, silently.</summary>
        public string AddressTown { get; set; } = "";

        /// <summary>Flattened from <c>Address.Postcode</c>.</summary>
        public string AddressPostcode { get; set; } = "";

        public List<ShelfDto> Shelves { get; set; } = [];
    }

    public sealed class ShelfDto
    {
        /// <summary>Renamed: <c>Code</c> on the entity.</summary>
        public string Reference { get; set; } = "";

        public int Capacity { get; set; }
    }

    public class CatalogueItemDto
    {
        public int Id { get; set; }

        public string Title { get; set; } = "";

        /// <summary>The enum, widened for the wire. AutoMapper needs a cast; DwarfMapper converts natively.</summary>
        public int Availability { get; set; }

        /// <summary>The enum as text. Which text is the question <c>DWARF083</c> asks.</summary>
        public string Format { get; set; } = "";

        /// <summary>Flattened from <c>Publisher.Name</c>.</summary>
        public string PublisherName { get; set; } = "";

        /// <summary>Populated only when the element's RUNTIME type is an audio book.</summary>
        public string? Narrator { get; set; }
    }

    public sealed class AudioBookDto : CatalogueItemDto
    {
        public int RuntimeMinutes { get; set; }
    }

    public sealed class MemberDto
    {
        public string DisplayName { get; set; } = "";

        public string CardNumber { get; set; } = "";

        /// <summary>A null source must not become null here — the substitute case.</summary>
        public string EmailAddress { get; set; } = "";

        public DateOnly JoinedOn { get; set; }
    }

    public sealed class LoanDto
    {
        public int Id { get; set; }

        public string ItemTitle { get; set; } = "";

        public string BorrowerCardNumber { get; set; } = "";

        public DateOnly TakenOn { get; set; }

        /// <summary>Guarded: only written once the loan is closed.</summary>
        public DateOnly? ReturnedOn { get; set; }

        /// <summary>Computed by a resolver rather than copied.</summary>
        public string FineDescription { get; set; } = "";
    }

    public sealed class PageDto<T>
    {
        public List<T> Items { get; set; } = [];

        public int PageNumber { get; set; }

        public int TotalPages { get; set; }
    }
}
