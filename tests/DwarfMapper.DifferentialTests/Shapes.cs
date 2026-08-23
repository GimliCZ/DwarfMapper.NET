// SPDX-License-Identifier: GPL-2.0-only

using System.ComponentModel;
using Riok.Mapperly.Abstractions;

namespace DwarfMapper.DifferentialTests
{
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
        [Description("in-progress")]
        InProgress,

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

// ── S9 value tuples as a mapped member ────────────────────────────────────────────────────────────────────
// Harvested gap (R18-29): value tuples were named in the fuzz schema as a TYPE and never mapped end-to-end
// against another mapper. Two members, because the interesting half is the second: element names are
// compile-time only — at runtime a tuple is Item1/Item2 — so a mapper that "renames" them is doing nothing,
// and one that silently reorders them would produce a result no member-by-member comparison of PROPERTIES
// could see. (MemberComparer had to learn to walk fields for this shape to assert anything at all.)
    public class TupleSrc
    {
        public (int Code, string Label) Pair { get; set; }

        public (int Code, string Label) Renamed { get; set; }
    }

    public class TupleDst
    {
        public (int Code, string Label) Pair { get; set; }

        // Different ELEMENT names, same shape. Legal C#, and the question is whether any mapper treats the names
        // as meaningful — they are erased before either mapper's output can be compared.
        public (int Id, string Text) Renamed { get; set; }
    }

// ── S10 polymorphic dispatch on the RUNTIME type ──────────────────────────────────────────────────────────
// Harvested gap (R18-29). The inventory named it "generic derived-type dispatch" after Mapperly's axis, and
// the GENERIC declaration form is n/a here by design — DWARF053, a generic mapping method cannot be
// completed by a source generator that must emit a concrete body. The SEMANTICS are the part worth
// comparing, and all three mappers express them: given a member declared as the base type holding a derived
// instance, does the derived DTO come back, with its extra member populated?
//
// The base is CONCRETE: Mapperly refuses an abstract base here (RMG013 — nothing to construct when
// the runtime type matches no arm), and a base that can itself be produced is the shape that actually
// distinguishes the mappers, since a wrong answer is a well-formed base object rather than a crash.
//
// This is the shape whose failure is invisible to a compiler: a mapper that quietly maps the base slices
// returns a well-formed object with data missing. MemberComparer reports it as a runtime-type difference.
    public class Command
    {
        public string Name { get; set; } = "";
    }

    public sealed class AliasCommand : Command
    {
        public string Alias { get; set; } = "";
    }

    public class CommandDto
    {
        public string Name { get; set; } = "";
    }

    public sealed class AliasCommandDto : CommandDto
    {
        public string Alias { get; set; } = "";
    }

    public class DispatchSrc
    {
        public Command Only { get; set; } = new AliasCommand();
    }

    public class DispatchDst
    {
        public CommandDto Only { get; set; } = new AliasCommandDto();
    }

// ── S11 an enum value that matches no destination member ──────────────────────────────────────────────────
// The names are COMPLETE on both sides, so nothing is reportable at build time — DwarfMapper's DWARF015 and
// Mapperly's RMG038 both check declared members and both pass here. The question is the value that cannot be
// checked: an undefined one, arriving from a cast, a database column or a wire format, which is where enums
// actually go wrong. Asserted in LoudRatherThanSilentTests rather than compared in the catalogue, because
// two of the three mappers answer by throwing.
    public enum Level
    {
        Low,

        High
    }

    public enum LevelDto
    {
        Low,

        High
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
    [GenerateMap<KeywordSrc, KeywordDst>]
    [GenerateMap<TupleSrc, TupleDst>]
    [GenerateMap<DispatchSrc, DispatchDst>]
    public partial class DwarfShapes
    {
        /// <summary>
        ///     Declared as a method rather than a class-level pair because the dispatch arms are method-level:
        ///     <c>[MapDerivedType]</c> registers the runtime types this map is allowed to see. The
        ///     <c>DispatchSrc</c> pair above then reuses this declared pair for its <c>Only</c> member.
        /// </summary>
        [MapDerivedType<AliasCommand, AliasCommandDto>]
        public partial CommandDto ToCommand(Command src);
    }

    /// <summary>The same enum shape under the parity switch, which is what Mapperly and AutoMapper both do.</summary>
    [DwarfMapper(EnumStringSource = EnumStringSource.Identifier)]
    [GenerateMap<StatusSrc, StatusDst>]
    public partial class DwarfShapesIdentifierEnums;

    /// <summary>Enum to enum by name — the default strategy, stated rather than assumed.</summary>
    [DwarfMapper(EnumStrategy = EnumStrategy.ByName)]
    [GenerateMap<Level, LevelDto>]
    public partial class DwarfEnumByName;

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

        public partial TupleDst ToTuple(TupleSrc src);

        // FULLY QUALIFIED, and it has to be. This file's namespace is DwarfMapper.DifferentialTests, so
        // DwarfMapper's own MapDerivedTypeAttribute is in scope through the enclosing namespace — and an
        // enclosing-namespace name beats a `using`-imported one in C# lookup. Written unqualified here, the
        // MAPPERLY mapper silently received DWARF's attribute, Mapperly ignored an attribute it never saw, and
        // the harness reported it as Mapperly losing the derived type. A differential oracle that misconfigures
        // its own oracle produces confident nonsense, so the qualification stays.
        [Riok.Mapperly.Abstractions.MapDerivedType<AliasCommand, AliasCommandDto>]
        public partial CommandDto ToCommand(Command src);

        public partial DispatchDst ToDispatch(DispatchSrc src);

        // Mapperly's by-name enum mapping, in both of the forms it offers: strict, and with a fallback for a
        // value that matches nothing. The fallback is the capability DwarfMapper has no equivalent of, which is
        // the whole reason this pair is here.
        [MapEnum(EnumMappingStrategy.ByName)]
        public partial LevelDto ToLevel(Level src);

        [MapEnum(EnumMappingStrategy.ByName,
            FallbackValue = LevelDto.Low)]
        public partial LevelDto ToLevelWithFallback(Level src);
    }

// ── S7 reserved-keyword member names ──────────────────────────────────────────────────────────────────────
// Harvested shape (R18-29), and the harvest's first genuine defect: BOTH DwarfMapper and Mapperly emitted the
// name unescaped — `class = src.class;`, which the C# compiler parses as a malformed event declaration. A DTO
// with a member called `@class` or `@event` is ordinary in code generated from a schema; nobody writing test
// fixtures by hand chooses to type one, which is exactly why an outside inventory was worth building.
// DwarfMapper's half is fixed (R18-30). Mapperly's is not, so the comparison runs against AutoMapper only.
    public class KeywordSrc
    {
        public string @class { get; set; } = "";

        public int @event { get; set; }

        public bool @operator { get; set; }
    }

    public class KeywordDst
    {
        public string @class { get; set; } = "";

        public int @event { get; set; }

        public bool @operator { get; set; }
    }

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
}
