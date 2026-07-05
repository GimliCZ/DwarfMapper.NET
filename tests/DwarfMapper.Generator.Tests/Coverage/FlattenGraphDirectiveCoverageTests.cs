// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Model;

// Coverage suite for DwarfMapper.Generator.Model.FlattenGraphDirective.
// Techniques: unit, adversary, deterministic, defensive, fixture.
namespace DwarfMapper.Generator.Tests.Coverage;

/// <summary>
///     Value-equality and construction tests for <c>FlattenGraphDirective</c> sealed record.
/// </summary>
public class FlattenGraphDirectiveCoverageTests
{
    // ─── Construction and property access ─────────────────────────────────────

    [Fact]
    public void Constructor_stores_all_properties()
    {
        var d = new FlattenGraphDirective("Nav", "Coll", "TraversalHelper", "ConverterHelper");
        Assert.Equal("Nav", d.SourceNavigation);
        Assert.Equal("Coll", d.TargetCollection);
        Assert.Equal("TraversalHelper", d.TraversalHelperName);
        Assert.Equal("ConverterHelper", d.ConverterHelperName);
    }

    // ─── Value equality: equal when all fields match ───────────────────────────

    [Fact]
    public void Equal_when_all_fields_match()
    {
        var a = new FlattenGraphDirective("N", "C", "T", "V");
        var b = new FlattenGraphDirective("N", "C", "T", "V");
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Not_equal_when_SourceNavigation_differs()
    {
        var a = new FlattenGraphDirective("Nav1", "C", "T", "V");
        var b = new FlattenGraphDirective("Nav2", "C", "T", "V");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Not_equal_when_TargetCollection_differs()
    {
        var a = new FlattenGraphDirective("N", "Coll1", "T", "V");
        var b = new FlattenGraphDirective("N", "Coll2", "T", "V");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Not_equal_when_TraversalHelperName_differs()
    {
        var a = new FlattenGraphDirective("N", "C", "Trav1", "V");
        var b = new FlattenGraphDirective("N", "C", "Trav2", "V");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Not_equal_when_ConverterHelperName_differs()
    {
        var a = new FlattenGraphDirective("N", "C", "T", "Conv1");
        var b = new FlattenGraphDirective("N", "C", "T", "Conv2");
        Assert.NotEqual(a, b);
    }

    // ─── IEquatable<FlattenGraphDirective> ────────────────────────────────────

    [Fact]
    public void IEquatable_Equals_null_returns_false()
    {
        var a = new FlattenGraphDirective("N", "C", "T", "V");
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void Equals_object_with_same_values_returns_true()
    {
        var a = new FlattenGraphDirective("N", "C", "T", "V");
        object b = new FlattenGraphDirective("N", "C", "T", "V");
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Equals_object_with_different_type_returns_false()
    {
        var a = new FlattenGraphDirective("N", "C", "T", "V");
        Assert.False(a.Equals("not a directive"));
    }

    // ─── Record with operator == / != ─────────────────────────────────────────

    [Fact]
    public void Operator_equals_true_for_equal_records()
    {
        var a = new FlattenGraphDirective("Nav", "Col", "Trv", "Cnv");
        var b = new FlattenGraphDirective("Nav", "Col", "Trv", "Cnv");
        Assert.True(a == b);
    }

    [Fact]
    public void Operator_not_equals_true_for_different_records()
    {
        var a = new FlattenGraphDirective("Nav1", "Col", "Trv", "Cnv");
        var b = new FlattenGraphDirective("Nav2", "Col", "Trv", "Cnv");
        Assert.True(a != b);
    }

    // ─── Deterministic: hashcode stable across calls ──────────────────────────

    [Fact]
    public void GetHashCode_is_stable_across_calls()
    {
        var a = new FlattenGraphDirective("Nav", "Col", "TravHelper", "ConvHelper");
        var h1 = a.GetHashCode();
        var h2 = a.GetHashCode();
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void Equal_records_have_same_hashcode()
    {
        var a = new FlattenGraphDirective("NavX", "ColX", "TravX", "ConvX");
        var b = new FlattenGraphDirective("NavX", "ColX", "TravX", "ConvX");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ─── Adversary: empty strings ─────────────────────────────────────────────

    [Fact]
    public void Empty_string_values_allowed_and_equal()
    {
        var a = new FlattenGraphDirective("", "", "", "");
        var b = new FlattenGraphDirective("", "", "", "");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Mixing_empty_and_non_empty_not_equal()
    {
        var a = new FlattenGraphDirective("", "", "", "");
        var b = new FlattenGraphDirective("x", "", "", "");
        Assert.NotEqual(a, b);
    }

    // ─── Defensive: ToString contains field values (record auto-generated) ────

    [Fact]
    public void ToString_contains_SourceNavigation_value()
    {
        var a = new FlattenGraphDirective("MyNav", "MyColl", "THelper", "CHelper");
        var str = a.ToString();
        Assert.Contains("MyNav", str, StringComparison.Ordinal);
    }

    // ─── Fixture: golden cases ────────────────────────────────────────────────

    [Fact]
    public void Fixture_list_and_array_helper_names_differ()
    {
        // When target is an array, ConverterHelperName != TraversalHelperName.
        var listDir = new FlattenGraphDirective("Children", "Items",
            "__DwarfMap_FlattenGraph_abc123",
            "__DwarfMap_FlattenGraph_abc123"); // same for List target
        var arrDir = new FlattenGraphDirective("Children", "Items",
            "__DwarfMap_FlattenGraph_abc123",
            "__DwarfMap_FlattenGraphArr_abc123"); // different for array target

        Assert.NotEqual(listDir, arrDir);
    }

    [Fact]
    public void Fixture_with_clause_produces_new_instance()
    {
        // C# 9 record 'with' expression — exercises the generated copy constructor.
        var a = new FlattenGraphDirective("Nav", "Coll", "Trav", "Conv");
        var b = a with { ConverterHelperName = "NewConv" };
        Assert.NotEqual(a, b);
        Assert.Equal("NewConv", b.ConverterHelperName);
        Assert.Equal("Nav", b.SourceNavigation); // unchanged fields preserved
    }
}
