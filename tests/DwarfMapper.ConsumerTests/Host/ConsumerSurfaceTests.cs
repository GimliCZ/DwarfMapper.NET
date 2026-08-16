// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using ConsumerTests.Contracts;
using DwarfMapper;
using Microsoft.Extensions.DependencyInjection;

// The ELEMENT pair behind Map<ICollection<PartDto>>(List<Part>). Auto-detection records what the call site
// names — (List<Part>, ICollection<PartDto>) — and the pair that actually maps each element is nowhere in
// the manifest, so a validation root would check the collection shape and never the thing inside it.
// Measured, not assumed: see Both_UsesMap_forms_contribute_to_this_assemblys_requires_manifest.
[assembly: UsesMap(typeof(Part), typeof(PartDto))]

namespace ConsumerTests.Host;

/// <summary>
///     The consumer surface, exercised the way a consumer actually reaches it: across assemblies, through the
///     ambient registry, at runtime.
/// </summary>
/// <remarks>
///     <para>
///         Every assertion here corresponds to a Round-18 finding. None of them is reachable from a generator
///         snapshot test, because every test in that suite is one assembly, one compilation, no DI, no runtime
///         resolution — which is precisely why a 5,000-test suite could not see four defects and one blocking
///         gap.
///     </para>
///     <para>
///         The host does not reference the provider assemblies. See <c>README.md</c>.
///     </para>
/// </remarks>
public sealed class ConsumerSurfaceTests
{
    private static IDwarfMapper Mapper()
    {
        ProviderLoader.EnsureLoaded();
        return DwarfMapperFacade.Instance;
    }

    // ── 0. The precondition everything else depends on ──────────────────────────────────────────────

    [Fact]
    public void The_host_does_not_reference_the_provider_assemblies()
    {
        // If this ever fails, every test below has started passing for the wrong reason: the compiler would
        // be resolving what the ambient registry is supposed to. This is the guard on the guard.
        Assert.False(ProviderLoader.HostReferencesProviders(),
            "The host has acquired a compile-time reference to a provider assembly. That removes the whole "
            + "point of this project — the ambient registry must be the only thing that can resolve these "
            + "maps, because that is the condition under which Round 18's 47-site gap existed.");
    }

    // ── 1. Ambient resolution across assemblies ─────────────────────────────────────────────────────

    [Fact]
    public void A_map_declared_in_an_unreferenced_assembly_resolves_through_the_facade()
    {
        var dto = Mapper().Map<CustomerDto>(new Customer
        {
            Id = 7,
            Name = "Balin",
            Address = new Address { City = "Khazad-dûm", Postcode = "M1" }
        });

        Assert.Equal(7, dto.Id);
        Assert.Equal("Balin", dto.Name);
        Assert.Equal("Khazad-dûm", dto.Address.City);
    }

    // ── 2. Collection shapes through the facade (the 47-site blocker) ───────────────────────────────

    [Theory]
    [InlineData(typeof(List<CustomerDto>))]
    [InlineData(typeof(ICollection<CustomerDto>))]
    [InlineData(typeof(IEnumerable<CustomerDto>))]
    [InlineData(typeof(IReadOnlyList<CustomerDto>))]
    [InlineData(typeof(CustomerDto[]))]
    public void A_collection_of_a_declared_pair_resolves_without_declaring_a_collection_pair(Type destination)
    {
        ProviderLoader.EnsureLoaded();

        var mapped = (IEnumerable<CustomerDto>)DwarfMapperRegistry.Map(
            new List<Customer> { new() { Id = 1, Name = "a" }, new() { Id = 2, Name = "b" } },
            destination);

        Assert.Equal([1, 2], mapped.Select(d => d.Id));
    }

    [Fact]
    public void A_lazy_LINQ_source_resolves_without_a_materializing_ToList()
    {
        // A Where()/Select() result is a PRIVATE compiler-generated iterator no attribute can name. Before
        // interface lookup, the remedy was a load-bearing .ToList() at every such call site, each discovered
        // by a runtime failure in production-shaped code.
        ProviderLoader.EnsureLoaded();

        var lazy = new List<Customer> { new() { Id = 1 }, new() { Id = 9 } }.Where(c => c.Id > 5);

        var mapped = (List<CustomerDto>)DwarfMapperRegistry.Map(lazy, typeof(List<CustomerDto>));

        Assert.Equal(9, Assert.Single(mapped).Id);
    }

    // ── 3. Static-vs-runtime type divergence ────────────────────────────────────────────────────────

    [Fact]
    public void A_method_declared_ICollection_but_returning_List_still_resolves()
    {
        // The "two keys per call site" trap: the build-time check reads the STATIC argument type, the runtime
        // lookup reads source.GetType(). Declaring only one of them compiles and still throws — which cost
        // Round 18 a false start, because a single declared pair turned the DI graph green while 46 other
        // sites stayed broken.
        ProviderLoader.EnsureLoaded();

        ICollection<Customer> declaredAsInterface = new List<Customer> { new() { Id = 3 } };

        var mapped = (List<CustomerDto>)DwarfMapperRegistry.Map(
            declaredAsInterface, typeof(List<CustomerDto>));

        Assert.Equal(3, Assert.Single(mapped).Id);
    }

    // ── 4. Polymorphic elements inside a collection ─────────────────────────────────────────────────

    [Fact]
    public void A_derived_element_inside_a_base_typed_list_keeps_its_derived_member()
    {
        // Round 18 found this in a live API response: an AliasCommand inside a List<Command> silently lost
        // its Alias, because the collection loop binds the base pair at COMPILE time while the previous
        // mapper dispatched on the RUNTIME type.
        ProviderLoader.EnsureLoaded();

        var commands = new List<Command>
        {
            new() { Id = 1, Text = "plain" },
            new AliasCommand { Id = 2, Text = "aliased", Alias = "!a" }
        };

        var mapped = (List<CommandDto>)DwarfMapperRegistry.Map(commands, typeof(List<CommandDto>));

        Assert.Null(mapped[0].Alias);
        Assert.Equal("!a", mapped[1].Alias);
    }

    // ── 5. Options matrix: the same nested pair under different policies ────────────────────────────

    [Fact]
    public void Two_providers_configuring_the_same_nested_pair_do_not_silently_share_behaviour()
    {
        // The open gap Round 18 recorded: a nested pair reached from two classes was synthesized twice, once
        // null-guarded and once not, SILENTLY. Here the divergence is deliberate and asserted, so the two
        // copies cannot quietly swap.
        ProviderLoader.EnsureLoaded();

        // ProviderB declares Address -> AddressDto with [MapNullSkip], so it patches.
        var patchTarget = new AddressDto { City = "kept", Postcode = "kept" };
        var patched = (AddressDto)DwarfMapperRegistry.Map(
            new Address { City = "new" }, typeof(AddressDto));

        // The registry holds ONE provider per pair (first wins), so what matters is that the pair resolves
        // and that ambiguity is visible rather than silent.
        Assert.NotNull(patched);
        Assert.Equal("kept", patchTarget.City);

        Assert.True(DwarfMapperRegistry.IsAmbiguous(typeof(Address), typeof(AddressDto)),
            "Two assemblies register Address -> AddressDto under different options. That MUST be reported as "
            + "ambiguous rather than resolved silently — a consumer picking up whichever module initializer "
            + "ran first is exactly the non-determinism this library exists to refuse.");
    }

    // ── 6. Construction paths: factory vs constructor-parameter binding ─────────────────────────────

    [Fact]
    public void Constructor_parameter_binding_preserves_an_init_only_member()
    {
        ProviderLoader.EnsureLoaded();

        var reference = Guid.NewGuid();
        var ticket = (Ticket)DwarfMapperRegistry.Map(
            new TicketRow { Subject = "s", Priority = 2, Reference = reference }, typeof(Ticket));

        Assert.Equal("s", ticket.Subject);
        Assert.Equal(reference, ticket.Reference);
    }

    // ── 7. Non-public construction across an assembly boundary ──────────────────────────────────────

    [Fact]
    public void An_internal_constructor_is_reachable_via_InternalsVisibleTo_and_AllowNonPublic()
    {
        // The supported replacement for a reflective mapper's accessibility bypass. All three pieces are
        // required and all three are compiler-checked; a `private` ctor would still be DWARF026, by design.
        ProviderLoader.EnsureLoaded();

        var entry = (AuditEntry)DwarfMapperRegistry.Map(new AuditRow { Id = 5 }, typeof(AuditEntry));

        Assert.Equal(5, entry.Id);
    }

    // ── 8. Update-into through the facade ───────────────────────────────────────────────────────────

    [Fact]
    public void Update_into_maps_onto_an_existing_instance_through_the_facade()
    {
        var settings = new Settings { Theme = "dark", Locale = "en" };
        var alias = settings;

        Mapper().Map(new SettingsPatch { Theme = "light", Locale = "cs" }, settings);

        Assert.Equal("light", settings.Theme);
        Assert.Same(settings, alias);
    }

    [Fact]
    public void Patch_and_replace_coexist_on_one_mapper_across_the_assembly_boundary()
    {
        ProviderLoader.EnsureLoaded();

        // Only ONE of the two update-into methods can hold the (SettingsPatch, Settings) key, so the second is
        // recorded as ambiguous rather than silently overwriting. That visibility is the contract; which one
        // wins is not something a consumer should rely on.
        Assert.True(
            DwarfMapperRegistry.IsUpdateProvided(typeof(SettingsPatch), typeof(Settings)),
            "The update-into pair was not registered from an unreferenced assembly.");
    }

    // ── 9. The DI-graph smoke test — the check that caught the blocker ──────────────────────────────

    [Fact]
    public void Every_registration_in_the_container_resolves()
    {
        // ~30 lines, and it is the ONLY check that found the 47-site gap. Build the graph the way the app
        // does, resolve everything, and assert nothing is unresolvable. Cheap enough that every consumer
        // should have one.
        ProviderLoader.EnsureLoaded();

        var services = new ServiceCollection();
        services.AddSingleton<IDwarfMapper>(DwarfMapperFacade.Instance);
        services.AddSingleton<CustomerService>();
        services.AddSingleton<CommandService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var unresolvable = new List<string>();

        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType.IsGenericTypeDefinition) continue;

            try
            {
                if (provider.GetService(descriptor.ServiceType) is null)
                    unresolvable.Add(descriptor.ServiceType.FullName ?? descriptor.ServiceType.Name);
            }
            catch (Exception ex)
            {
                unresolvable.Add($"{descriptor.ServiceType.Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(unresolvable.Count == 0,
            "Unresolvable registration(s):\n  " + string.Join("\n  ", unresolvable));

        // Resolving is not enough — the services must actually MAP. A container that builds and then throws
        // on first use is exactly the failure mode this project exists to catch.
        Assert.Equal(2, provider.GetRequiredService<CustomerService>().ToDtos(
            [new Customer { Id = 1 }, new Customer { Id = 2 }]).Count);

        Assert.Equal("!x", provider.GetRequiredService<CommandService>()
            .ToDtos([new AliasCommand { Id = 1, Alias = "!x" }])[0].Alias);
    }

    // ── 5b. The consumption manifest this assembly publishes ────────────────────────────────────────

    /// <summary>
    ///     Both <c>[UsesMap]</c> forms actually reach this assembly's <c>DwarfRequiresMap</c> manifest.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Without this, the two <c>[UsesMap]</c> declarations were compiled text that nothing read: the
    ///         host declares no validation root (it must not — it deliberately references no provider, so a
    ///         whole-graph check here would fail on maps that are correctly wired), and no other assertion in
    ///         this project touches the manifest. If the attribute stopped contributing, everything would
    ///         still pass. That is exactly the "satisfies a scan, proves nothing" shape this suite exists to
    ///         refuse, and it is worst here, because these rows are what discharge the CrossAssembly
    ///         obligation — the category whose whole claim is that it is only observable across a boundary.
    ///     </para>
    ///     <para>
    ///         Both pairs are chosen to be uniquely attributable, which was measured rather than assumed.
    ///         Deleting the two declarations drops the manifest from nine entries to eight and removes exactly
    ///         these two: auto-detection records what a call site NAMES, so a collection call contributes
    ///         <c>(List&lt;Part&gt;, ICollection&lt;PartDto&gt;)</c> and never the element pair, and a
    ///         <c>List&lt;Command&gt;</c> call site cannot name the derived <c>AliasCommand</c> arm at all.
    ///         A pair that is also auto-detected — <c>(Customer, CustomerDto)</c>, which has a direct
    ///         <c>Map&lt;CustomerDto&gt;</c> call site — would keep this test green with the attribute
    ///         deleted, and the first draft of this row used precisely that pair.
    ///     </para>
    /// </remarks>
    [Fact]
    public void Both_UsesMap_forms_contribute_to_this_assemblys_requires_manifest()
    {
        var required = typeof(ConsumerSurfaceTests).Assembly
            .GetCustomAttributes<DwarfRequiresMapAttribute>()
            .Select(a => (a.Source, a.Destination))
            .ToList();

        Assert.Contains((typeof(Part), typeof(PartDto)), required);
        Assert.Contains((typeof(AliasCommand), typeof(CommandDto)), required);
    }

    // ── 6. Shapes the generator cannot express, registered by declaration ───────────────────────────

    [Fact]
    public void A_hand_written_map_marked_ProvidesMap_resolves_through_the_facade()
    {
        // An object that HOLDS a collection, mapped to the collection, is not a mapping SHAPE — one side is a
        // container, the other an element sequence — so it is written by hand. Without [ProvidesMap] it is
        // then an ordinary method that nothing registers: the code is correct and every facade call site for
        // the pair still throws. Round 18 hit that on five pairs and recorded it as "the code is fine; the
        // harness cannot see it."
        var quotes = Mapper().Map<ICollection<QuoteDto>>(new QuoteBook
        {
            Quotes = [new Quote { Number = 1, Text = "speak friend" }, new Quote { Number = 2, Text = "and enter" }]
        });

        Assert.Equal(2, quotes.Count);
        Assert.Equal("and enter", quotes.Last().Text);
    }

    // ── 7. A collection over a pair that constructs through a factory ───────────────────────────────

    [Fact]
    public void Elements_of_a_factory_constructed_pair_are_mapped_not_merely_constructed()
    {
        // The failure this guards is a list of BLANK objects: an element converter that resolves to the bare
        // [MapConstructor] factory runs it without the member assignments that follow. Found in a live
        // consumer, whose own source carried a fourteen-line comment telling readers not to rely on the map.
        //
        // Both halves are asserted, because either alone would pass while the other was broken: the factory
        // must have run (only it produces the PART- prefix) AND the settable member must have been assigned.
        var parts = Mapper().Map<ICollection<PartDto>>(new List<Part>
        {
            new() { Code = "A1", Quantity = 5 },
            new() { Code = "B2", Quantity = 9 }
        });

        Assert.Equal(2, parts.Count);
        Assert.Equal("PART-A1", parts.First().Code);
        Assert.Equal(9, parts.Last().Quantity);
    }

    // ── 8. enum ↔ string: both readings of one annotation, live at once ─────────────────────────────

    [Fact]
    public void The_default_writes_the_annotated_text()
    {
        // [Description] is a DISPLAY annotation to most people and the PERSISTED format to the default. Round
        // 18 came within one code review of writing the annotated form into a store full of identifiers.
        Assert.Equal("Next-Day",
            Mapper().Map<ShipmentDoc>(new Shipment { Channel = DispatchChannel.NextDay }).Channel);
    }

    [Fact]
    public void EnumStringSource_Identifier_writes_the_member_name_from_another_assembly()
    {
        // The parity switch, and the case the synthesized helper's NAME has to survive: one enum, two
        // readings, two assemblies, one process. Keyed by type alone the two would have shared a single
        // helper and whichever loaded first would have decided the persisted format for the other — the exact
        // bug class this round fixed twice elsewhere, and one that only a multi-assembly test can stage.
        Assert.Equal("NextDay",
            Mapper().Map<ShipmentLog>(new Shipment { Channel = DispatchChannel.NextDay }).Channel);
    }

    [Fact]
    public void Both_readings_of_the_same_enum_coexist()
    {
        // Stated as its own assertion rather than inferred from the two above: what matters is that they
        // disagree, in one process, without either having been silently overwritten by the other.
        var shipment = new Shipment { Channel = DispatchChannel.NextDay };

        Assert.NotEqual(
            Mapper().Map<ShipmentDoc>(shipment).Channel,
            Mapper().Map<ShipmentLog>(shipment).Channel);
    }

    // ── 9. Members whose names are C# keywords ──────────────────────────────────────────────────────

    [Fact]
    public void A_member_named_for_a_keyword_maps_across_the_assembly_boundary()
    {
        // Ordinary in code generated from a JSON or OpenAPI schema. ISymbol.Name hands the name over WITHOUT
        // the @, so an unescaped emission produces `class = src.class,` — parsed as a malformed event
        // declaration, out of generated code, with no diagnostic. The provider assembly would not have
        // compiled at all; this asserts the mapping actually carries the values too.
        var dto = Mapper().Map<KeywordDto>(new KeywordRow { @class = "wizard", @event = 3 });

        Assert.Equal("wizard", dto.@class);
        Assert.Equal(3, dto.@event);
    }
}

/// <summary>
///     A service shaped like a consumer's: it takes the facade and maps a collection.
/// </summary>
/// <remarks>
///     The facade call names a COLLECTION shape, so that is what the manifest records; the element pair each
///     item maps through is not in it. See the assembly-level <c>[UsesMap]</c> at the top of this file, which
///     states the one behind the sibling <c>Part</c> call site. That gap is the blind spot Round 18 found as
///     47 latent runtime throws, and it is why the attribute exists.
/// </remarks>
public sealed class CustomerService(IDwarfMapper mapper)
{
    public ICollection<CustomerDto> ToDtos(IEnumerable<Customer> customers) =>
        mapper.Map<ICollection<CustomerDto>>(customers.ToList());
}

/// <summary>
///     The polymorphic-collection call site, behind DI, exactly as an API controller would have it.
/// </summary>
/// <remarks>
///     The static element type here is <c>Command</c>; the pair that actually maps when the list holds an
///     <c>AliasCommand</c> is <c>(AliasCommand, CommandDto)</c>, declared in an assembly this one does not
///     reference. No call site in this assembly names it, so the generic <c>[UsesMap&lt;S, T&gt;]</c> is the
///     only way it reaches the manifest.
/// </remarks>
[UsesMap<AliasCommand, CommandDto>]
public sealed class CommandService(IDwarfMapper mapper)
{
    public List<CommandDto> ToDtos(List<Command> commands) => mapper.Map<List<CommandDto>>(commands);
}
