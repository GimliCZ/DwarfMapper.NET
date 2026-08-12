// SPDX-License-Identifier: GPL-2.0-only

namespace ConsumerTests.Contracts;

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
