// SPDX-License-Identifier: GPL-2.0-only

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
        Assert.Equal("Secret", attr.Target);
    }

    [Fact]
    public void MapPropertyAttribute_exposes_source_and_target()
    {
        var attr = new MapPropertyAttribute("Src", "Dst");
        Assert.Equal("Src", attr.Source);
        Assert.Equal("Dst", attr.Target);
    }

    [Fact]
    public void MapPropertyAttribute_allows_multiple_on_methods_and_members()
    {
        // Unified attribute: method form ([MapProperty(src, tgt)] for the class model) + member form
        // ([MapProperty(dest)] for the [MapTo] registry).
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(MapPropertyAttribute), typeof(AttributeUsageAttribute))!;
        Assert.True(usage.AllowMultiple);
        Assert.Equal(
            AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field,
            usage.ValidOn);
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
        Assert.Equal(EnumStrategy.ByValue,
            new DwarfMapperAttribute { EnumStrategy = EnumStrategy.ByValue }.EnumStrategy);
    }

    [Fact]
    public void DwarfMapperAttribute_nullStrategy_defaults_Throw()
    {
        Assert.Equal(NullStrategy.Throw, new DwarfMapperAttribute().NullStrategy);
        Assert.Equal(NullStrategy.SetDefault,
            new DwarfMapperAttribute { NullStrategy = NullStrategy.SetDefault }.NullStrategy);
    }

    [Fact]
    public void FlattenAttribute_exposes_source_and_allows_multiple()
    {
        Assert.Equal("Address", new FlattenAttribute("Address").SourceMember);
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(FlattenAttribute), typeof(AttributeUsageAttribute))!;
        Assert.True(usage.AllowMultiple);
        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
    }

    [Fact]
    public void Hook_attributes_target_methods()
    {
        foreach (var t in new[] { typeof(BeforeMapAttribute), typeof(AfterMapAttribute) })
        {
            var u = (AttributeUsageAttribute)Attribute.GetCustomAttribute(t, typeof(AttributeUsageAttribute))!;
            Assert.Equal(AttributeTargets.Method, u.ValidOn);
        }
    }

    [Fact]
    public void RoundTripAttribute_targets_methods()
    {
        var u = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(RoundTripAttribute), typeof(AttributeUsageAttribute))!;
        Assert.Equal(AttributeTargets.Method, u.ValidOn);
    }

    [Fact]
    public void ReinterpretAttribute_targets_methods_allows_multiple()
    {
        Assert.Equal("V", new ReinterpretAttribute("V").Member);
        var u = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(ReinterpretAttribute), typeof(AttributeUsageAttribute))!;
        Assert.True(u.AllowMultiple);
        Assert.Equal(AttributeTargets.Method, u.ValidOn);
    }

    [Fact]
    public void MapConstructorAttribute_exposes_method_and_allows_multiple_on_classes()
    {
        var attr = new MapConstructorAttribute<int, string>("Make");
        Assert.Equal("Make", attr.Method);
        var u = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(MapConstructorAttribute<,>), typeof(AttributeUsageAttribute))!;
        Assert.True(u.AllowMultiple);
        Assert.Equal(AttributeTargets.Class, u.ValidOn);
    }

    // ISSUE-014 — the attribute gates compared `ContainingNamespace?.Name` against "DwarfMapper".
    // INamespaceSymbol.Name is the LAST SEGMENT only, so a user's own `Acme.DwarfMapper` namespace satisfied the
    // check and a same-named user attribute was treated as one of ours. Now compared against the full display
    // string. Here `Acme.DwarfMapper.GenerateMap<,>` must NOT be picked up as a mapping directive.
    [Fact]
    public void A_user_attribute_in_a_namespace_ending_in_DwarfMapper_is_not_mistaken_for_ours()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Acme.DwarfMapper
                         {
                             [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                             public sealed class GenerateMapAttribute<TSource, TTarget> : System.Attribute { }
                         }
                         namespace Demo
                         {
                             public class Src { public int A { get; set; } }
                             public class Dst { public int A { get; set; } }

                             [global::DwarfMapper.DwarfMapper]
                             [Acme.DwarfMapper.GenerateMap<Src, Dst>]
                             public partial class M { }
                         }
                         """;
        var (_, generated) = GeneratorTestHarness.Run(s);

        // The foreign attribute must not produce a Src->Dst map.
        Assert.DoesNotContain("Dst Map(", generated, StringComparison.Ordinal);
    }
}
