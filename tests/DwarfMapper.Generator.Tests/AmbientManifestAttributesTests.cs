// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Contract tests for the ambient cross-assembly mapping attributes: the generator-emitted manifests
///     (<see cref="DwarfProvidesMapAttribute" /> / <see cref="DwarfRequiresMapAttribute" />), the user-facing
///     consumption marker (<see cref="UsesMapAttribute" /> / <see cref="UsesMapAttribute{TSource,TDestination}" />),
///     and the validation-root marker (<see cref="DwarfMapperValidationRootAttribute" />). These pin their
///     AttributeUsage so the generator's emission/reading (later ambient-registry phases) can rely on it.
/// </summary>
public sealed class AmbientManifestAttributesTests
{
    private static AttributeUsageAttribute Usage<T>() where T : Attribute
    {
        return (AttributeUsageAttribute)Attribute.GetCustomAttribute(typeof(T), typeof(AttributeUsageAttribute))!;
    }

    [Fact]
    public void ProvidesMap_and_RequiresMap_are_assembly_targeted_multiple()
    {
        foreach (var usage in new[] { Usage<DwarfProvidesMapAttribute>(), Usage<DwarfRequiresMapAttribute>() })
        {
            Assert.Equal(AttributeTargets.Assembly, usage.ValidOn);
            Assert.True(usage.AllowMultiple);
            Assert.False(usage.Inherited);
        }

        var provides = new DwarfProvidesMapAttribute(typeof(int), typeof(string));
        Assert.Equal(typeof(int), provides.Source);
        Assert.Equal(typeof(string), provides.Destination);

        var requires = new DwarfRequiresMapAttribute(typeof(string), typeof(int));
        Assert.Equal(typeof(string), requires.Source);
        Assert.Equal(typeof(int), requires.Destination);
    }

    [Fact]
    public void UsesMap_generic_and_nongeneric_target_assembly_and_class()
    {
        var nonGeneric = Usage<UsesMapAttribute>();
        var generic = Usage<UsesMapAttribute<int, string>>();

        foreach (var usage in new[] { nonGeneric, generic })
        {
            Assert.Equal(AttributeTargets.Assembly | AttributeTargets.Class, usage.ValidOn);
            Assert.True(usage.AllowMultiple);
        }

        var attr = new UsesMapAttribute(typeof(int), typeof(string));
        Assert.Equal(typeof(int), attr.Source);
        Assert.Equal(typeof(string), attr.Destination);
    }

    [Fact]
    public void ValidationRoot_is_single_assembly_marker_with_AutoValidate()
    {
        var usage = Usage<DwarfMapperValidationRootAttribute>();
        Assert.Equal(AttributeTargets.Assembly, usage.ValidOn);
        Assert.False(usage.AllowMultiple);

        var attr = new DwarfMapperValidationRootAttribute { AutoValidate = true };
        Assert.True(attr.AutoValidate);
        Assert.False(new DwarfMapperValidationRootAttribute().AutoValidate);
    }
}
