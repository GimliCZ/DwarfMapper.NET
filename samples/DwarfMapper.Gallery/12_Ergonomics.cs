// SPDX-License-Identifier: GPL-2.0-only

// 12 — Calling styles: the generated extension method and DI.
// Beyond `new Mapper().ToDto(x)`, DwarfMapper generates (by default) a `x.ToGemDto()` extension named after the
// target type, and — when Microsoft.Extensions.DependencyInjection is referenced — an AddDwarfMappers() that
// registers every mapper as a (stateless) singleton with NO reflection or assembly scan. Opt the extensions out
// per mapper with [DwarfMapper(GenerateExtensions = false)].
using System;
using DwarfMapper;
using DwarfMapper.Extensions;                       // surfaces the generated ToGemDto() extension
using Microsoft.Extensions.DependencyInjection;

namespace DwarfMapper.Gallery.Ex12;

public sealed class Gem { public string Kind { get; set; } = ""; public int Carats { get; set; } }
public sealed class GemDto { public string Kind { get; set; } = ""; public int Carats { get; set; } }

[DwarfMapper]
public partial class Mapper
{
    public partial GemDto ToDto(Gem g);
}

public static class Example
{
    public static void Run()
    {
        Gem gem = new Gem { Kind = "Arkenstone", Carats = 999 };

        // (a) generated extension method (named after the target type):
        GemDto viaExtension = gem.ToGemDto();

        // (b) dependency injection — one registration call, then inject the mapper:
        using ServiceProvider provider = new ServiceCollection().AddDwarfMappers().BuildServiceProvider();
        GemDto viaDi = provider.GetRequiredService<Mapper>().ToDto(gem);

        Console.WriteLine($"12 Facade + DI        -> ext: {viaExtension.Kind}; di: {viaDi.Kind} ({viaDi.Carats} ct)");
    }
}
