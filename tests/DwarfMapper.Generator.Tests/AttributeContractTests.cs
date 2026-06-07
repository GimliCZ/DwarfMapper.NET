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
}
