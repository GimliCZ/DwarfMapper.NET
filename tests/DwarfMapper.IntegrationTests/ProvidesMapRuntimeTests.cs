// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;

namespace DwarfMapper.IntegrationTests;

public sealed class QuoteData
{
    public string Text { get; set; } = "";
}

public sealed class QuoteDocument
{
    public string Text { get; set; } = "";
}

/// <summary>
///     An object that HOLDS a collection. Mapping it to the collection itself is not a DwarfMapper mapping
///     shape — one side is a container, the other an element sequence — so it must be written by hand.
/// </summary>
public sealed class UserQuotesDocument
{
    public string Id { get; set; } = "";
    public List<QuoteDocument> Quotes { get; set; } = [];
}

[DwarfMapper]
[GenerateMap<QuoteDocument, QuoteData>]
public partial class QuoteMappers
{
    /// <summary>
    ///     Hand-written because the shape is object-to-collection. Marked <c>[ProvidesMap]</c> so it is
    ///     reachable through the ambient facade like any generated map.
    /// </summary>
    [ProvidesMap]
    public ICollection<QuoteData> ToQuotes(UserQuotesDocument document)
    {
        // The generated maps emit this guard themselves; a hand-written one has to carry its own, which is a
        // fair reminder that [ProvidesMap] registers YOUR code — the registry does not wrap it.
        ArgumentNullException.ThrowIfNull(document);

        return document.Quotes.Select(Map).ToList();
    }

    /// <summary>The static form — invoked on the type, so it needs no cached instance.</summary>
    [ProvidesMap]
    public static string Describe(UserQuotesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return $"{document.Id}:{document.Quotes.Count}";
    }
}

/// <summary>
///     <c>[ProvidesMap]</c> — registering a hand-written method into the ambient registry.
/// </summary>
/// <remarks>
///     Some conversions are legitimately not mapping SHAPES, and once written by hand they are ordinary
///     methods: nothing self-registers them, so every facade call site throws and a parity harness reports
///     them as "not registered". Round 18 hit this on five pairs and recorded the conclusion plainly — "the
///     code is fine; the harness cannot see it". This is how a consumer says so.
/// </remarks>
public sealed class ProvidesMapRuntimeTests
{
    private static void EnsureRegistered() =>
        RuntimeHelpers.RunModuleConstructor(typeof(QuoteMappers).Module.ModuleHandle);

    private static UserQuotesDocument Document() => new()
    {
        Id = "u1",
        Quotes = [new() { Text = "a" }, new() { Text = "b" }]
    };

    [Fact]
    public void A_hand_written_object_to_collection_map_resolves_through_the_registry()
    {
        EnsureRegistered();

        var quotes = (ICollection<QuoteData>)DwarfMapperRegistry.Map(
            Document(), typeof(ICollection<QuoteData>));

        Assert.Equal(["a", "b"], quotes.Select(q => q.Text));
    }

    [Fact]
    public void A_static_hand_written_map_needs_no_cached_instance()
    {
        EnsureRegistered();

        var described = (string)DwarfMapperRegistry.Map(Document(), typeof(string));

        Assert.Equal("u1:2", described);
    }

    [Fact]
    public void The_pair_is_reported_as_provided_so_DWARF061_can_see_it()
    {
        // The manifest half of the fix. Registering at runtime but staying absent from the Provides manifest
        // would leave the validation root reporting a missing map for a pair that IS registered.
        EnsureRegistered();

        Assert.True(DwarfMapperRegistry.IsProvided(
            typeof(UserQuotesDocument), typeof(ICollection<QuoteData>)));
    }

    [Fact]
    public void The_element_map_it_delegates_to_still_works_on_its_own()
    {
        EnsureRegistered();

        var mapped = (QuoteData)DwarfMapperRegistry.Map(new QuoteDocument { Text = "x" }, typeof(QuoteData));

        Assert.Equal("x", mapped.Text);
    }
}
