// SPDX-License-Identifier: GPL-2.0-only

using System.ComponentModel;

// members named with C# keywords (@class, @event) are the escaped-identifier shape under test
// ReSharper disable InconsistentNaming
namespace ConsumerTests.Contracts
{
    // ── The plain shapes a consumer already has ──────────────────────────────────────────────────────────
// Deliberately ordinary: no DwarfMapper attributes anywhere in this assembly. A consumer's POCOs stay
// untouched by the mapper, and this project proves it by not referencing a single mapper type.

    public sealed class Customer
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public Address Address { get; set; } = new();
    }

    public sealed class CustomerDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public AddressDto Address { get; set; } = new();
    }

    /// <summary>
    ///     Nested, and reachable from BOTH providers — which is the point. The two providers configure it under
    ///     different policy options, so a synthesized copy in each must not silently diverge.
    /// </summary>
    public sealed class Address
    {
        public string? City { get; set; }

        public string? Postcode { get; set; }
    }

    public sealed class AddressDto
    {
        public string? City { get; set; }

        public string? Postcode { get; set; }
    }

// ── Polymorphism: a base and a derived, mapped inside a collection ───────────────────────────────────

    public class Command
    {
        public int Id { get; set; }

        public string Text { get; set; } = "";
    }

    public sealed class AliasCommand : Command
    {
        public string Alias { get; set; } = "";
    }

    public sealed class CommandDto
    {
        public int Id { get; set; }

        public string Text { get; set; } = "";

        /// <summary>Populated only when the element's RUNTIME type is an alias — the divergence Round 18 found.</summary>
        public string? Alias { get; set; }
    }

// ── Construction paths: factory vs constructor-parameter binding ─────────────────────────────────────

    public sealed class TicketRow
    {
        public string Subject { get; set; } = "";

        public int Priority { get; set; }

        public Guid Reference { get; set; }
    }

    /// <summary>
    ///     <c>Reference</c> is <c>init</c>-only, so a <c>[MapConstructor]</c> factory cannot assign it and the
    ///     mapped value is dropped. Direct construction fills an object initializer, where it survives.
    /// </summary>
    public sealed class Ticket
    {
        public Ticket(string subject, int priority)
        {
            Subject = subject;
            Priority = priority;
        }

        public string Subject { get; }

        public int Priority { get; }

        public Guid Reference { get; init; }
    }

// ── Non-public construction: the supported replacement for a reflective mapper's bypass ──────────────

    public sealed class AuditRow
    {
        public int Id { get; set; }
    }

    public sealed class AuditEntry
    {
        /// <summary>
        ///     <c>internal</c>, not <c>private</c>: a deliberate grant the compiler checks. Reached from another
        ///     assembly only because of the <c>[InternalsVisibleTo]</c> in this project's csproj — which is
        ///     exactly the migration path away from a <c>private</c> "ctor for the mapper".
        /// </summary>
        internal AuditEntry()
        {
        }

        public int Id { get; set; }
    }

// ── Patch-merge: the shape behind a PATCH endpoint ───────────────────────────────────────────────────

    public sealed class SettingsPatch
    {
        public string? Theme { get; set; }

        public string? Locale { get; set; }
    }

    public sealed class Settings
    {
        public string? Theme { get; set; }

        public string? Locale { get; set; }
    }

// ── An object that HOLDS a collection, mapped to the collection ──────────────────────────────────────
// Not a DwarfMapper mapping shape: one side is a container, the other an element sequence. It has to be
// written by hand — and a hand-written method is an ordinary method that nothing registers, so every facade
// call site for the pair throws while the code is perfectly correct. That is what [ProvidesMap] is for.

    public sealed class Quote
    {
        public int Number { get; set; }

        public string Text { get; set; } = "";
    }

    public sealed class QuoteDto
    {
        public int Number { get; set; }

        public string Text { get; set; } = "";
    }

    public sealed class QuoteBook
    {
        public List<Quote> Quotes { get; set; } = [];
    }

// ── A collection over a pair that constructs through a factory ───────────────────────────────────────
// The element route must run the factory AND the member assignments. A generator that adopts the factory as
// the element converter instead emits `result.Add(Create(item))` — the bare factory, no members — and if the
// factory ignores its argument the call returns a list of blanks. Silent, total data loss, green build.

    public sealed class Part
    {
        public string Code { get; set; } = "";

        public int Quantity { get; set; }
    }

    public sealed class PartDto
    {
        /// <summary>Private, so the pair genuinely needs the factory rather than merely being given one.</summary>
        private PartDto(string code)
        {
            Code = code;
        }

        public string Code { get; }

        public int Quantity { get; set; }

        /// <summary>The tell: a value only the factory can produce, so "did the factory run" is observable.</summary>
        public static PartDto FromCode(string code)
        {
            return new PartDto("PART-" + code);
        }
    }

// ── enum ↔ string, and the two things the annotation can mean ────────────────────────────────────────
// [Description] is overwhelmingly a DISPLAY annotation, and under the default it becomes the PERSISTED
// string. Which of the two a consumer wants is a per-mapper decision, and the wrong one silently rewrites
// every row it touches.

    public enum DispatchChannel
    {
        [Description("Next-Day")]
        NextDay,

        Standard
    }

    public sealed class Shipment
    {
        public DispatchChannel Channel { get; set; }
    }

    /// <summary>Written under the default: the annotation decides.</summary>
    public sealed class ShipmentDoc
    {
        public string Channel { get; set; } = "";
    }

    /// <summary>Written under <c>EnumStringSource.Identifier</c>: the member name decides.</summary>
    public sealed class ShipmentLog
    {
        public string Channel { get; set; } = "";
    }

// ── Members whose names are C# keywords ──────────────────────────────────────────────────────────────
// Ordinary in code generated from a JSON or OpenAPI schema, where `class` and `event` are perfectly good
// field names. ISymbol.Name hands them over WITHOUT the `@`, so an unescaped emission produces
// `class = src.class,` — parsed as a malformed event declaration, out of generated code, with no diagnostic.

    public sealed class KeywordRow
    {
        public string @class { get; set; } = "";

        public int @event { get; set; }
    }

    public sealed class KeywordDto
    {
        public string @class { get; set; } = "";

        public int @event { get; set; }
    }
}
