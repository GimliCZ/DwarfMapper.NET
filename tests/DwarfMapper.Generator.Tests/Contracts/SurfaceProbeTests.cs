// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Contracts;

public sealed class SurfaceProbeTests
{
    [Fact]
    public void A_MapIgnore_on_a_create_map_is_observed_as_honoured()
    {
        var element = SurfaceCatalog.Elements.Single(e =>
            string.Equals(e.UsageName, "MapIgnore", StringComparison.Ordinal)
            && e.Type.GetGenericArguments().Length == 0);
        var c = new SurfaceCase(element, AttributeTargets.Method, "[MapIgnore(\"Name\")]", "ctor(1)");

        var (effect, detail) = SurfaceProbe.Classify(c, Endpoint.CreateMap);

        Assert.True(effect is SurfaceEffect.Honoured or SurfaceEffect.Refused,
            $"[MapIgnore] at CreateMap classified as {effect} ({detail}). If this reads Silent, the probe is "
            + "not observing correctly and every cell built on it is vacuous.");
    }

    [Fact]
    public void A_class_only_attribute_on_a_method_is_NotCompilable()
    {
        // Pins AttributeUsage to reality. Nothing else in the repository does: the declaration says
        // AttributeTargets.Class and no test ever tries the illegal site to confirm the compiler agrees.
        var element = SurfaceCatalog.Elements.Single(e =>
            string.Equals(e.UsageName, "DwarfMapper", StringComparison.Ordinal));
        var c = new SurfaceCase(element, AttributeTargets.Method, "[DwarfMapper]", "illegal-site");

        var (effect, _) = SurfaceProbe.Classify(c, Endpoint.CreateMap);

        Assert.Equal(SurfaceEffect.NotCompilable, effect);
    }
}
