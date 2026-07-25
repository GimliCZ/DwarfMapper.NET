// SPDX-License-Identifier: GPL-2.0-only
#nullable enable

using Xunit;

namespace DwarfMapper.IntegrationTests;

// The polymorphic silent drop.
//
// A source member is DECLARED as a concrete base class (`Animal`), but at run time it holds a DERIVED instance
// (`Dog`). A compile-time mapper can only see the declared type, so it copies the base members and constructs a
// base DTO — and every derived member is dropped, silently, with no diagnostic and no runtime error. The caller
// gets a plausible-looking object that is quietly missing data.
//
// This is the exact failure DwarfMapper's whole "never silent" premise exists to prevent, and it is the one
// place where being compile-time is a genuine disadvantage versus a reflective mapper (which can dispatch on
// the runtime type). DWARF033 already refuses an ABSTRACT or INTERFACE source in auto-nesting — but a CONCRETE
// base class is perfectly instantiable, so it sails straight through.
//
// [MapDerivedType] is the supported answer. This fixture PINS the drop so the behaviour is a deliberate,
// tested fact rather than an accident, and so the DWARF071 diagnostic that warns about it has a runtime
// counterpart proving why it is worth warning about.

public class Animal
{
    public string Name { get; set; } = "";
}

public sealed class Dog : Animal
{
    public string Breed { get; set; } = "";
}

public sealed class AnimalDto
{
    public string Name { get; set; } = "";
}

public sealed class KennelSrc
{
    public Animal Pet { get; set; } = new();
}

public sealed class KennelDst
{
    public AnimalDto Pet { get; set; } = new();
}

// Raises DWARF071 — which is the point. It is an Info, so it never breaks the build; no suppression needed.
[DwarfMapper]
public partial class KennelMapper
{
    public partial KennelDst Map(KennelSrc src);
}

public class PolymorphicSilentDropRuntimeTests
{
    [Fact]
    public void A_derived_instance_is_mapped_as_its_declared_base_type()
    {
        var src = new KennelSrc { Pet = new Dog { Name = "Balin", Breed = "Terrier" } };

        var result = new KennelMapper().Map(src);

        // The base member survives...
        Assert.Equal("Balin", result.Pet.Name);

        // ...and `Breed` is simply gone. Not an exception, not a diagnostic at run time — just absent. That is
        // why DWARF071 warns at BUILD time: this is the one thing the developer cannot discover by testing the
        // happy path, because the happy path looks fine.
        Assert.IsType<AnimalDto>(result.Pet);
    }
}
