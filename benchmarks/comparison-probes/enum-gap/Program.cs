// SPDX-License-Identifier: GPL-2.0-only
// Round 35 T25 enum-gap probe: the by-value enum row of the performance table read DwarfMapper 10.8 ns against Mapperly's
// 5.2 ns on Linux, and level on Windows. This rebuilds the row's two generated methods verbatim (DwarfMapper's
// DwarfEnumByValueM.Map at 72d85a1, Mapperly 4.3.1's MapEnum), then each with the other's argument handling and with
// other forms of the null check, called the way MapperBenchmarks calls them. EnumCast times the conversion expressions
// alone. Results and what they mean: ../../results/2026-11-15-round35-t25-comparison.md.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromTypes(new[] { typeof(EnumCast), typeof(MapShapes) }).Run(args);

public enum Status { Pending, Active, Closed }

// Declared in a different order, as the suite's BenchStatusDto is: a cast by value is not a match by name.
public enum StatusDto { Closed, Pending, Active }

public sealed class Src
{
    public int Id { get; set; }

    public Status Status { get; set; }
}

public sealed class Dst
{
    public int Id { get; set; }

    public StatusDto Status { get; set; }
}

public class EnumCast
{
    private readonly Status[] _ring = new Status[512];
    private int _i;

    [GlobalSetup]
    public void Setup()
    {
        for (var i = 0; i < _ring.Length; i++) _ring[i] = (Status)(i % 3);
    }

    // DwarfMapper's emitted conversion: (Tgt)int.CreateChecked((int)v)
    [Benchmark(Baseline = true)]
    public StatusDto CreateChecked() => (StatusDto)int.CreateChecked((int)_ring[_i++ & 511]);

    // Through the underlying type, without the overflow check
    [Benchmark]
    public StatusDto PlainCast() => (StatusDto)(int)_ring[_i++ & 511];

    // Mapperly's emitted conversion: (Tgt)v
    [Benchmark]
    public StatusDto DirectCast() => (StatusDto)_ring[_i++ & 511];
}

public sealed class DwarfShape
{
    // Verbatim
    public Dst Map(Src src)
    {
        ArgumentNullException.ThrowIfNull(src);
        return new Dst { Id = src.Id, Status = EnumVal(src.Status) };
    }

    public Dst MapUnchecked(Src src) => new Dst { Id = src.Id, Status = EnumVal(src.Status) };

    // These two helpers are deliberately NOT [DoesNotReturn] (the variant being measured), so the compiler cannot see
    // that src is non-null after the check; the ! emits no code and leaves the measured method unchanged.
    public Dst MapThrowHelper(Src src)
    {
        if (src is null) Throw();
        return new Dst { Id = src!.Id, Status = EnumVal(src.Status) };
    }

    public Dst MapNamedHelper(Src src)
    {
        if (src is null) ThrowNamed("src");
        return new Dst { Id = src!.Id, Status = EnumVal(src.Status) };
    }

    public Dst MapNoReturnHelper(Src src)
    {
        if (src is null) ThrowNoReturn();
        return new Dst { Id = src.Id, Status = EnumVal(src.Status) };
    }

    public Dst MapNamedNoReturnHelper(Src src)
    {
        if (src is null) ThrowNamedNoReturn("src");
        return new Dst { Id = src.Id, Status = EnumVal(src.Status) };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Throw() => throw new ArgumentNullException("src");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNamed(string paramName) => throw new ArgumentNullException(paramName);

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNoReturn() => throw new ArgumentNullException("src");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNamedNoReturn(string paramName) => throw new ArgumentNullException(paramName);

    private static StatusDto EnumVal(Status v) => (StatusDto)int.CreateChecked((int)v);
}

public sealed class MapperlyShape
{
    // Verbatim
    public Dst MapEnum(Src s)
    {
        var target = new Dst();
        target.Id = s.Id;
        target.Status = (StatusDto)s.Status;
        return target;
    }

    public Dst MapEnumChecked(Src s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var target = new Dst();
        target.Id = s.Id;
        target.Status = (StatusDto)s.Status;
        return target;
    }
}

[MemoryDiagnoser]
public class MapShapes
{
    private readonly DwarfShape _dwarf = new();
    private readonly MapperlyShape _mapperly = new();
    private readonly Src[] _ring = new Src[512];
    private int _i;

    [GlobalSetup]
    public void Setup()
    {
        for (var i = 0; i < _ring.Length; i++) _ring[i] = new Src { Id = i, Status = (Status)(i % 3) };
    }

    private Src Next() => _ring[_i++ & 511];

    [Benchmark(Baseline = true)]
    public Dst Mapperly() => _mapperly.MapEnum(Next());

    [Benchmark]
    public Dst MapperlyWithThrowIfNull() => _mapperly.MapEnumChecked(Next());

    [Benchmark]
    public Dst Dwarf() => _dwarf.Map(Next());

    [Benchmark]
    public Dst DwarfWithoutCheck() => _dwarf.MapUnchecked(Next());

    [Benchmark]
    public Dst DwarfWithThrowHelper() => _dwarf.MapThrowHelper(Next());

    [Benchmark]
    public Dst DwarfWithNamedHelper() => _dwarf.MapNamedHelper(Next());

    [Benchmark]
    public Dst DwarfWithNoReturnHelper() => _dwarf.MapNoReturnHelper(Next());

    [Benchmark]
    public Dst DwarfWithNamedNoReturnHelper() => _dwarf.MapNamedNoReturnHelper(Next());
}
