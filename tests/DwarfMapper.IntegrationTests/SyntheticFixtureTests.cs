// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// ─────────────────────────────────────────────────────────────────────────────────────────────────
// Synthetic fixture matrix for constructor-linking and function-mapping: required / optional / params
// constructor parameters, init-only & settable members, public vs internal access, and synthetic
// converter/factory functions of varying arity (0-arg, 1-arg, params) referenced from mappings.
// One target per host (one source → many targets would collide on a single Map overload).
// ─────────────────────────────────────────────────────────────────────────────────────────────────

public sealed class SynSrc
{
    public int A { get; set; } = 11;
    public string B { get; set; } = "bee";
    public int C { get; set; } = 33;
}

/// <summary>Public parameterized ctor, get-only members.</summary>
public sealed class SynPublicCtor
{
    public SynPublicCtor(int a, string b)
    {
        A = a;
        B = b;
    }

    public int A { get; }
    public string B { get; }
}

/// <summary>Optional ctor parameter — the source has no 'note', so it is omitted and the default is used.</summary>
public sealed class SynOptionalCtor
{
    public SynOptionalCtor(int a, string note = "fallback")
    {
        A = a;
        Note = note;
    }

    public int A { get; }
    public string Note { get; }
}

/// <summary>params ctor tail — the source has no 'rest', so it is omitted (empty array).</summary>
public sealed class SynParamsCtor
{
    public SynParamsCtor(int a, params int[] rest)
    {
        A = a;
        Rest = rest;
    }

    public int A { get; }
    public int[] Rest { get; }
}

/// <summary>required + init-only members alongside a settable one.</summary>
public sealed class SynRequiredCtor
{
    public required int A { get; init; }
    public required string B { get; init; }
    public int C { get; set; }
}

/// <summary>Internal parameterized ctor — reachable only with AllowNonPublic.</summary>
public sealed class SynInternalCtor
{
    internal SynInternalCtor(int a, string b)
    {
        A = a;
        B = b;
    }

    public int A { get; }
    public string B { get; }
}

/// <summary>Internal property setters — writable only with AllowNonPublic.</summary>
public sealed class SynInternalMembers
{
    public int A { get; internal set; }
    public string B { get; internal set; } = "";
}

[DwarfMapper(CaseInsensitive = true)]
[GenerateMap<SynSrc, SynPublicCtor>]
public partial class SynPublicCtorMapper
{
}

[DwarfMapper(CaseInsensitive = true)]
[GenerateMap<SynSrc, SynOptionalCtor>]
public partial class SynOptionalCtorMapper
{
}

[DwarfMapper(CaseInsensitive = true)]
[GenerateMap<SynSrc, SynParamsCtor>]
public partial class SynParamsCtorMapper
{
}

[DwarfMapper(CaseInsensitive = true)]
[GenerateMap<SynSrc, SynRequiredCtor>]
public partial class SynRequiredCtorMapper
{
}

[DwarfMapper(CaseInsensitive = true, AllowNonPublic = true)]
[GenerateMap<SynSrc, SynInternalCtor>]
public partial class SynInternalCtorMapper
{
}

[DwarfMapper(CaseInsensitive = true, AllowNonPublic = true)]
[GenerateMap<SynSrc, SynInternalMembers>]
public partial class SynInternalMembersMapper
{
}

// ── Synthetic functions of varying arity, referenced from a mapping ──
public sealed class FnSrc
{
    public string Name { get; set; } = "alice";
    public int X { get; set; } = 5;
}

public sealed class FnTarget
{
    public string Name { get; set; } = "";
    public string Tag { get; set; } = "";
    public int Summed { get; set; }
}

[DwarfMapper]
[GenerateMap<FnSrc, FnTarget>]
[MapProperty<FnSrc, FnTarget>(nameof(FnSrc.Name), nameof(FnTarget.Name), Use = nameof(Upper))]
[MapValue<FnTarget>(nameof(FnTarget.Tag), Use = nameof(Stamp))]
[MapProperty<FnSrc, FnTarget>(nameof(FnSrc.X), nameof(FnTarget.Summed), Use = nameof(Triple))]
public partial class SynFunctionMapper
{
    private static string Upper(string s)
    {
        return s.ToUpperInvariant();
        // 1-arg converter
    }

    private static string Stamp()
    {
        return "stamped";
        // 0-arg value provider
    }

    private static int Triple(int x)
    {
        return Sum(x, x, x);
        // references a params helper
    }

    private static int Sum(params int[] xs)
    {
        var t = 0;
        foreach (var n in xs) t += n;
        return t;
    } // any-arity
}

/// <summary>Factory function referencing a params helper.</summary>
public sealed class FacTarget
{
    public FacTarget(int seeded)
    {
        Seeded = seeded;
    }

    public int Seeded { get; }
    public string Name { get; set; } = "";
}

[DwarfMapper]
[GenerateMap<FnSrc, FacTarget>]
[MapConstructor<FnSrc, FacTarget>(nameof(MakeFac))]
public partial class SynFactoryMapper
{
    private static FacTarget MakeFac(FnSrc s)
    {
        return new FacTarget(SumAll(s.X, 1, 2, 3, 4));
    }

    private static int SumAll(params int[] xs)
    {
        var t = 0;
        foreach (var n in xs) t += n;
        return t;
    }
}

public sealed class SyntheticFixtureTests
{
    [Fact]
    public void Public_parameterized_ctor()
    {
        var d = new SynPublicCtorMapper().Map(new SynSrc());
        Assert.Equal(11, d.A);
        Assert.Equal("bee", d.B);
    }

    [Fact]
    public void Optional_ctor_param_is_omitted_and_defaulted()
    {
        var d = new SynOptionalCtorMapper().Map(new SynSrc());
        Assert.Equal(11, d.A);
        Assert.Equal("fallback", d.Note); // optional 'note' had no source → default
    }

    [Fact]
    public void Params_ctor_tail_is_omitted_and_empty()
    {
        var d = new SynParamsCtorMapper().Map(new SynSrc());
        Assert.Equal(11, d.A);
        Assert.Empty(d.Rest); // params 'rest' had no source → empty
    }

    [Fact]
    public void Required_and_init_members_are_set()
    {
        var d = new SynRequiredCtorMapper().Map(new SynSrc());
        Assert.Equal(11, d.A);
        Assert.Equal("bee", d.B);
        Assert.Equal(33, d.C);
    }

    [Fact]
    public void Internal_ctor_is_used_with_AllowNonPublic()
    {
        var d = new SynInternalCtorMapper().Map(new SynSrc());
        Assert.Equal(11, d.A);
        Assert.Equal("bee", d.B);
    }

    [Fact]
    public void Internal_member_setters_are_written_with_AllowNonPublic()
    {
        var d = new SynInternalMembersMapper().Map(new SynSrc());
        Assert.Equal(11, d.A);
        Assert.Equal("bee", d.B);
    }

    [Fact]
    public void Functions_of_varying_arity_are_referenced()
    {
        var d = new SynFunctionMapper().Map(new FnSrc());
        Assert.Equal("ALICE", d.Name); // 1-arg
        Assert.Equal("stamped", d.Tag); // 0-arg
        Assert.Equal(15, d.Summed); // Triple -> Sum(5,5,5)
    }

    [Fact]
    public void Factory_referencing_a_params_helper()
    {
        var d = new SynFactoryMapper().Map(new FnSrc());
        Assert.Equal(15, d.Seeded); // SumAll(5,1,2,3,4)
    }
}
