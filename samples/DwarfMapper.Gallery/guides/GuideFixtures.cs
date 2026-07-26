// SPDX-License-Identifier: GPL-2.0-only

// Fixtures shared by the guide examples (30+). Named to match the vocabulary the README and the migration
// guides already use — Customer, Order, Address — so a snippet lifted into that prose reads as if it had been
// written there.
//
// WHY SO MANY NEAR-IDENTICAL TARGETS (OrderRow / OrderView / OrderDto / OrderReceipt): the library allows only
// one ambient provider per (source, target) pair — a second STATELESS mapper for the same pair is DWARF063, a
// Warning, and warnings are errors here. So each example that needs its own mapper also needs its own target
// type. A mapper with constructor dependencies is exempt (DWARF062, Info: not ambient-registered at all),
// which is why RatedOrderMapper can share OrderDto with ReversibleOrderMapper and StampedOrderMapper cannot.
//
// Unconsumed SOURCE members are fine throughout: RequiredMapping defaults to Target, so only every
// destination member must be mapped. That is why the *Row/*View/*Summary targets can ignore Customer.Address.

namespace DwarfMapper.Gallery.Guides;

public sealed class Address
{
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

public sealed class Customer
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public decimal Total { get; set; }
    public Address Address { get; set; } = new();
}

/// <summary>Composite target (example 30): renamed member, converted member, flattened address.</summary>
public sealed class CustomerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Total { get; set; } = "";
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

/// <summary>Auto-matching target (example 31): every member pairs by name, so the pair needs no config.</summary>
public sealed class CustomerRow
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public decimal Total { get; set; }
}

/// <summary>Ambient-facade target (example 35).</summary>
public sealed class CustomerSummary
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
}

public sealed class Order
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public decimal Total { get; set; }
}

/// <summary>Auto-matching target (example 31).</summary>
public sealed class OrderRow
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public decimal Total { get; set; }
}

/// <summary>Auto-matching target (example 32), reached through the generated <c>ToOrderView()</c>.</summary>
public sealed class OrderView
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public decimal Total { get; set; }
}

/// <summary>Renamed target shared by the reversible and rate-converting mappers (example 34).</summary>
public sealed class OrderDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Total { get; set; }
    public string Source { get; set; } = "";
}

/// <summary>Target for the [AfterMap] mapper (example 34), which must not share OrderDto — see the note above.</summary>
public sealed class OrderReceipt
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Total { get; set; }
    public string Source { get; set; } = "";
}
