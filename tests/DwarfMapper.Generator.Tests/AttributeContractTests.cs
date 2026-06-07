// SPDX-License-Identifier: GPL-2.0-only
using System;
using DwarfMapper;

namespace DwarfMapper.Generator.Tests;

public class AttributeContractTests
{
    [Fact]
    public void DwarfMapperAttribute_targets_classes_only()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(DwarfMapperAttribute), typeof(AttributeUsageAttribute))!;
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void MapIgnoreAttribute_exposes_target_member()
    {
        var attr = new MapIgnoreAttribute("Secret");
        Assert.Equal("Secret", attr.TargetMember);
    }

    [Fact]
    public void MapPropertyAttribute_exposes_source_and_target()
    {
        var attr = new MapPropertyAttribute("Src", "Dst");
        Assert.Equal("Src", attr.Source);
        Assert.Equal("Dst", attr.Target);
    }

    [Fact]
    public void MapPropertyAttribute_allows_multiple_on_methods()
    {
        var usage = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(MapPropertyAttribute), typeof(System.AttributeUsageAttribute))!;
        Assert.True(usage.AllowMultiple);
        Assert.Equal(System.AttributeTargets.Method, usage.ValidOn);
    }

    [Fact]
    public void DwarfMapperAttribute_caseInsensitive_defaults_false()
    {
        Assert.False(new DwarfMapperAttribute().CaseInsensitive);
    }

    [Fact]
    public void MapPropertyAttribute_Use_defaults_null_and_is_settable()
    {
        Assert.Null(new MapPropertyAttribute("a", "b").Use);
        Assert.Equal("Conv", new MapPropertyAttribute("a", "b") { Use = "Conv" }.Use);
    }

    [Fact]
    public void DwarfMapperAttribute_enumStrategy_defaults_ByName()
    {
        Assert.Equal(EnumStrategy.ByName, new DwarfMapperAttribute().EnumStrategy);
        Assert.Equal(EnumStrategy.ByValue, new DwarfMapperAttribute { EnumStrategy = EnumStrategy.ByValue }.EnumStrategy);
    }

    [Fact]
    public void DwarfMapperAttribute_nullStrategy_defaults_Throw()
    {
        Assert.Equal(NullStrategy.Throw, new DwarfMapperAttribute().NullStrategy);
        Assert.Equal(NullStrategy.SetDefault, new DwarfMapperAttribute { NullStrategy = NullStrategy.SetDefault }.NullStrategy);
    }

    [Fact]
    public void FlattenAttribute_exposes_source_and_allows_multiple()
    {
        Assert.Equal("Address", new FlattenAttribute("Address").SourceMember);
        var usage = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(FlattenAttribute), typeof(System.AttributeUsageAttribute))!;
        Assert.True(usage.AllowMultiple);
        Assert.Equal(System.AttributeTargets.Method, usage.ValidOn);
    }

    [Fact]
    public void Hook_attributes_target_methods()
    {
        foreach (var t in new[] { typeof(BeforeMapAttribute), typeof(AfterMapAttribute) })
        {
            var u = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(t, typeof(System.AttributeUsageAttribute))!;
            Assert.Equal(System.AttributeTargets.Method, u.ValidOn);
        }
    }

    [Fact]
    public void RoundTripAttribute_targets_methods()
    {
        var u = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(RoundTripAttribute), typeof(System.AttributeUsageAttribute))!;
        Assert.Equal(System.AttributeTargets.Method, u.ValidOn);
    }
}
