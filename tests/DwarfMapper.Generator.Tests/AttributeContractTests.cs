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
}
